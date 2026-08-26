// One audio file, opened as a stream of interleaved 32-bit float frames.
//
// The Source Reader is asked for float PCM rather than for whatever the file
// happens to hold, so it inserts the decoder — and a converter when the codec
// hands back integers — and every format reaches the mixer in the same shape.
// Sample rate and channel count are whatever the file is; resampling to the
// device rate is the output stage's job, not this one's.
using System;
using System.Runtime.InteropServices;

namespace Retrace
{
    sealed class Decoder : IDisposable
    {
        IMFSourceReader reader;
        // What ReadSample handed back last, and how much of it has been taken.
        float[] pending = new float[0];
        int pendingOffset, pendingCount;
        bool ended;

        public int Channels { get; private set; }
        public int SampleRate { get; private set; }
        /// <summary>Seconds, or 0 when the container does not declare it.</summary>
        public double Duration { get; private set; }
        /// <summary>kbps as declared by the source, or 0 when it is not known.</summary>
        public int Bitrate { get; private set; }
        public bool Ended { get { return ended && pendingCount == 0; } }

        Decoder() { }

        /// <summary>
        /// Opens a file, or returns null if Windows has no decoder for it. A
        /// missing codec is an ordinary outcome here — .ogg and .opus only decode
        /// where the Web Media Extensions are installed — so it is reported as a
        /// null rather than thrown.
        /// </summary>
        public static Decoder Open(string path)
        {
            if (!Mf.Startup()) return null;
            var d = new Decoder();
            try
            {
                if (Mf.MFCreateSourceReaderFromURL(path, null, out d.reader) < 0) return null;
                if (!d.Configure()) { d.Dispose(); return null; }
                return d;
            }
            catch (COMException) { d.Dispose(); return null; }
            catch (InvalidCastException) { d.Dispose(); return null; }
        }

        bool Configure()
        {
            // Deselect everything first: a video file's picture stream would
            // otherwise be decoded alongside the audio for nothing.
            reader.SetStreamSelection(Mf.AllStreams, false);
            if (reader.SetStreamSelection(Mf.FirstAudioStream, true) < 0) return false;

            IMFMediaType want;
            if (Mf.MFCreateMediaType(out want) < 0) return false;
            try
            {
                want.SetGUID(ref Mf.MF_MT_MAJOR_TYPE, ref Mf.MFMediaType_Audio);
                want.SetGUID(ref Mf.MF_MT_SUBTYPE, ref Mf.MFAudioFormat_Float);
                want.SetUINT32(ref Mf.MF_MT_AUDIO_BITS_PER_SAMPLE, 32);
                if (reader.SetCurrentMediaType(Mf.FirstAudioStream, IntPtr.Zero, want) < 0)
                    return false;
            }
            finally { Mf.Release(want); }

            // Read back what the reader actually settled on rather than assuming
            // the request was honoured verbatim: it keeps the file's own rate and
            // channel count, and only the sample format was ours to choose.
            IMFMediaType actual;
            if (reader.GetCurrentMediaType(Mf.FirstAudioStream, out actual) < 0) return false;
            try
            {
                int channels, rate;
                if (actual.GetUINT32(ref Mf.MF_MT_AUDIO_NUM_CHANNELS, out channels) < 0) return false;
                if (actual.GetUINT32(ref Mf.MF_MT_AUDIO_SAMPLES_PER_SECOND, out rate) < 0) return false;
                if (channels <= 0 || rate <= 0) return false;
                Channels = channels;
                SampleRate = rate;
            }
            finally { Mf.Release(actual); }

            Duration = ReadDouble(Mf.MF_PD_DURATION) / 10000000.0;
            Bitrate = (int)Math.Round(ReadDouble(Mf.MF_PD_AUDIO_ENCODING_BITRATE) / 1000.0);
            return true;
        }

        long ReadDoubleRaw(Guid key, out bool ok)
        {
            PropVariant pv;
            ok = false;
            if (reader.GetPresentationAttribute(Mf.MediaSource, ref key, out pv) < 0) return 0;
            long v = pv.longValue;
            Mf.PropVariantClear(ref pv);
            ok = true;
            return v;
        }

        double ReadDouble(Guid key)
        {
            bool ok;
            long v = ReadDoubleRaw(key, out ok);
            return ok && v > 0 ? v : 0;
        }

        /// <summary>
        /// Fills <paramref name="dest"/> with up to <paramref name="count"/>
        /// interleaved floats and returns how many were written. A short return is
        /// only ever the end of the file — the loop below keeps pulling until the
        /// request is met or the stream runs out.
        /// </summary>
        public int Read(float[] dest, int offset, int count)
        {
            int written = 0;
            while (written < count)
            {
                if (pendingCount == 0)
                {
                    if (ended) break;
                    if (!Pull()) break;
                    continue;
                }
                int take = Math.Min(count - written, pendingCount);
                Array.Copy(pending, pendingOffset, dest, offset + written, take);
                pendingOffset += take;
                pendingCount -= take;
                written += take;
            }
            return written;
        }

        /// <summary>Reads one sample from the reader into the pending buffer.
        /// False means nothing more will come.</summary>
        bool Pull()
        {
            int actualIndex, flags;
            long timestamp;
            IMFSample sample;
            int hr = reader.ReadSample(Mf.FirstAudioStream, 0, out actualIndex,
                out flags, out timestamp, out sample);
            if (hr < 0) { ended = true; return false; }

            if ((flags & Mf.EndOfStream) != 0)
            {
                ended = true;
                Mf.Release(sample);
                return false;
            }

            // A null sample with no end-of-stream flag is a gap the reader wants
            // skipped (a discontinuity, or a format change it has already applied).
            // It is not the end, so report progress and let the caller come back.
            if (sample == null) return true;

            IMFMediaBuffer buf = null;
            try
            {
                if (sample.ConvertToContiguousBuffer(out buf) < 0) return true;
                IntPtr p;
                int maxLen, curLen;
                if (buf.Lock(out p, out maxLen, out curLen) < 0) return true;
                try
                {
                    int floats = curLen / 4;
                    if (floats <= 0) return true;
                    if (pending.Length < floats) pending = new float[floats];
                    Marshal.Copy(p, pending, 0, floats);
                    pendingOffset = 0;
                    pendingCount = floats;
                }
                finally { buf.Unlock(); }
            }
            finally
            {
                Mf.Release(buf);
                Mf.Release(sample);
            }
            return true;
        }

        /// <summary>Moves playback to <paramref name="seconds"/> and drops
        /// whatever was already decoded past that point.</summary>
        public void Seek(double seconds)
        {
            if (reader == null) return;
            if (seconds < 0) seconds = 0;
            var pos = new PropVariant();
            pos.vt = PropVariant.VT_I8;
            pos.longValue = (long)(seconds * 10000000.0);
            if (reader.SetCurrentPosition(ref Mf.TimeFormatNone, ref pos) < 0) return;
            pendingOffset = 0;
            pendingCount = 0;
            // A seek past the end still succeeds; the next Pull is what discovers
            // there is nothing there. Clearing the flag is what lets it try.
            ended = false;
        }

        public void Dispose()
        {
            Mf.Release(reader);
            reader = null;
            pending = new float[0];
            pendingCount = 0;
        }
    }
}
