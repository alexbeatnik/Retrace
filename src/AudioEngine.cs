// Playback itself: one thread per track that opens the file, pulls PCM from the
// decoder, folds it to stereo, runs it through the equaliser, takes the levels
// the meters are drawn from, and hands 16-bit frames to the device — which
// blocks it, and is therefore what paces the whole loop to real time.
//
// The decoder is created, used and destroyed entirely on that thread, and never
// touched from anywhere else. This is not tidiness: a Source Reader built on the
// UI thread is bound to its single-threaded apartment, and every call to it from
// the audio thread then has to cross apartments. That marshalling throws, the
// catch below reads the exception as the end of the file, and the symptom is a
// track that loads, reports its duration correctly, and stops instantly. Opening
// on the thread that reads keeps both in the same apartment and the problem
// cannot occur. Play() therefore hands the path over and waits for the thread to
// report back rather than opening anything itself.
//
// Everything the UI reads (position, levels, the analyser window) is written
// from the audio thread and read from the message loop, so each crossing is
// either an interlocked scalar or guarded by `levelLock`. The UI never blocks on
// the audio thread and the audio thread never touches a control.
using System;
using System.Threading;

namespace Retrace
{
    enum PlayState { Stopped, Playing, Paused }

    sealed class AudioEngine : IDisposable
    {
        /// <summary>
        /// Frames per device block. About 23 ms at 44.1 kHz: short enough that the
        /// meters follow the music rather than lagging visibly behind it, long
        /// enough that the decode thread is not woken forty times a second for
        /// nothing.
        /// </summary>
        const int BlockFrames = 1024;
        const int BlockCount = 4;

        /// <summary>How long Play() will wait for the file to open before giving
        /// up on it. Generous for a local file, short enough that a dead network
        /// share does not freeze the window.</summary>
        const int OpenTimeoutMs = 5000;

        /// <summary>Window handed to the FFT. 1024 bins at 44.1 kHz is a 43 Hz
        /// resolution — enough to separate the bass bands of the display.</summary>
        public const int AnalyserSize = 1024;

        readonly Equalizer equalizer = new Equalizer();
        readonly object levelLock = new object();
        readonly float[] analyser = new float[AnalyserSize];
        readonly ManualResetEvent opened = new ManualResetEvent(false);
        int analyserFill;

        Thread worker;
        WaveOut output;
        volatile bool stopping;
        volatile bool paused;
        volatile bool openOk;
        volatile int stateRaw;          // PlayState, read by the UI
        volatile float peakLeft, peakRight;

        string requestPath;
        double requestStart;
        double seekBase;                 // seconds the device's own counter is measured from
        volatile int pendingSeekMs = -1; // a seek asked for while the thread is running
        long positionTicks;              // Interlocked: current position in milliseconds

        float volume = 0.7f;
        float balance;

        /// <summary>Raised on the audio thread when a track reaches its end of its
        /// own accord. Stop and track changes do not raise it.</summary>
        public event EventHandler TrackEnded;

        public Equalizer Eq { get { return equalizer; } }
        public PlayState State { get { return (PlayState)stateRaw; } }
        public string Path { get; private set; }
        public int SampleRate { get; private set; }
        public int SourceChannels { get; private set; }
        public int Bitrate { get; private set; }
        public double Duration { get; private set; }

        public double Position
        {
            get { return Interlocked.Read(ref positionTicks) / 1000.0; }
        }

        public float Volume
        {
            get { return volume; }
            set { volume = value < 0 ? 0 : (value > 1 ? 1 : value); }
        }

        public float Balance
        {
            get { return balance; }
            set { balance = value < -1 ? -1 : (value > 1 ? 1 : value); }
        }

        /// <summary>Left and right needle deflection, already mapped onto the
        /// meter's printed scale.</summary>
        public void Levels(out float left, out float right)
        {
            left = Audio.MeterScale(peakLeft);
            right = Audio.MeterScale(peakRight);
        }

        /// <summary>
        /// Copies the most recent analyser window out for the spectrum display.
        /// A copy rather than a reference: the audio thread overwrites this buffer
        /// every block, and a half-updated window drawn mid-write shows as a tear
        /// across the display.
        /// </summary>
        public bool CopyAnalyser(float[] dest)
        {
            if (dest == null || dest.Length < AnalyserSize) return false;
            lock (levelLock)
            {
                if (analyserFill < AnalyserSize) return false;
                Array.Copy(analyser, dest, AnalyserSize);
            }
            return true;
        }

        /// <summary>
        /// Starts <paramref name="path"/>, replacing whatever was playing. False
        /// means Windows could not decode the file — the caller skips to the next
        /// track rather than stalling on it.
        /// </summary>
        public bool Play(string path, double startAt)
        {
            Stop();
            if (string.IsNullOrEmpty(path)) return false;

            requestPath = path;
            requestStart = startAt;
            openOk = false;
            opened.Reset();
            stopping = false;
            paused = false;
            pendingSeekMs = -1;
            Silence();
            stateRaw = (int)PlayState.Playing;

            worker = new Thread(Run);
            worker.IsBackground = true;
            worker.Name = "Retrace audio";
            // Above normal: a decode thread that misses its slot produces an
            // audible gap, and it sleeps blocked on the device the rest of the time.
            worker.Priority = ThreadPriority.AboveNormal;
            worker.Start();

            // The open happens on that thread; this waits for its verdict so the
            // caller still gets a straight yes or no.
            if (!opened.WaitOne(OpenTimeoutMs) || !openOk)
            {
                Stop();
                return false;
            }
            return true;
        }

        public void Pause()
        {
            if (stateRaw != (int)PlayState.Playing) return;
            WaveOut o = output;
            if (o == null) return;
            paused = true;
            o.Pause();
            stateRaw = (int)PlayState.Paused;
            Silence();
        }

        public void Resume()
        {
            if (stateRaw != (int)PlayState.Paused) return;
            WaveOut o = output;
            if (o == null) return;
            paused = false;
            o.Resume();
            stateRaw = (int)PlayState.Playing;
        }

        public void Stop()
        {
            stopping = true;
            Thread t = worker;
            WaveOut o = output;
            // Wake the writer out of its wait before joining, or the join sits out
            // the device's whole queue.
            if (o != null) o.Flush();
            if (t != null && t.IsAlive && t != Thread.CurrentThread)
            {
                // A device that has wedged must not take the window down with it;
                // after this the worker is abandoned and its own finally clause
                // cleans up whenever it comes back.
                t.Join(1500);
            }
            worker = null;

            // The decoder belongs to the worker and is disposed there. Only the
            // device is ours to close. A worker abandoned by the join above may
            // still be inside Write, so the closing is safe because WaveOut
            // refuses every use of its buffers once disposed — not because the
            // join is assumed to have won.
            output = null;
            if (o != null) o.Dispose();

            stateRaw = (int)PlayState.Stopped;
            Interlocked.Exchange(ref positionTicks, 0);
            Silence();
        }

        void Silence()
        {
            peakLeft = 0;
            peakRight = 0;
            lock (levelLock) { analyserFill = 0; Array.Clear(analyser, 0, analyser.Length); }
        }

        /// <summary>
        /// Requests a new position. The move is performed on the audio thread at
        /// the top of its next block: seeking the decoder from the UI thread while
        /// the worker is inside ReadSample is both a data race and a call into the
        /// wrong apartment.
        /// </summary>
        public void Seek(double seconds)
        {
            if (stateRaw == (int)PlayState.Stopped) return;
            if (seconds < 0) seconds = 0;
            if (Duration > 0 && seconds > Duration) seconds = Duration;
            // Reported straight away so the display and the slider follow the hand
            // that is dragging them, not the thread that has yet to notice.
            Interlocked.Exchange(ref positionTicks, (long)(seconds * 1000));
            pendingSeekMs = (int)(seconds * 1000);
        }

        // ---- The audio thread --------------------------------------------------

        void Run()
        {
            Decoder decoder = null;
            bool ranOut = false;
            try
            {
                WaveOut o;
                decoder = Open(out o);
                if (decoder == null || o == null)
                {
                    openOk = false;
                    opened.Set();
                    return;
                }
                openOk = true;
                opened.Set();

                ranOut = Loop(decoder, o);
            }
            catch (Exception)
            {
                // A device pulled out mid-track (a USB DAC, a Bluetooth headset)
                // surfaces here. Treat it as the end of the track: the alternative
                // is an unhandled exception on a background thread, which takes the
                // whole process down.
                ranOut = true;
            }
            finally
            {
                // Never leave Play() blocked, whatever went wrong above.
                opened.Set();
                if (decoder != null) decoder.Dispose();
                peakLeft = 0;
                peakRight = 0;
            }

            if (ranOut && !stopping)
            {
                stateRaw = (int)PlayState.Stopped;
                EventHandler h = TrackEnded;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        /// <summary>Opens the file and the device, and publishes what the UI needs
        /// to know about them. Null means Windows has no decoder for it.</summary>
        Decoder Open(out WaveOut device)
        {
            device = null;
            Decoder d = Decoder.Open(requestPath);
            if (d == null) return null;

            try
            {
                // The device is opened at the file's own rate and always in
                // stereo: Windows' own mixer resamples to the endpoint far better
                // than anything worth writing here, and a fixed two channels keeps
                // WAVE_FORMAT_PCM valid for the 5.1 files that would otherwise
                // need WAVE_FORMAT_EXTENSIBLE.
                device = new WaveOut(d.SampleRate, 2, BlockFrames, BlockCount);
            }
            catch (InvalidOperationException)
            {
                d.Dispose();
                return null;
            }

            output = device;
            Path = requestPath;
            SampleRate = d.SampleRate;
            SourceChannels = d.Channels;
            Bitrate = d.Bitrate;
            Duration = d.Duration;

            equalizer.SetSampleRate(d.SampleRate);
            equalizer.Reset();

            if (requestStart > 0) { d.Seek(requestStart); seekBase = requestStart; }
            else seekBase = 0;
            Interlocked.Exchange(ref positionTicks, (long)(seekBase * 1000));
            return d;
        }

        /// <summary>The decode loop. True means the track played to its end;
        /// false means it was stopped or the device went away.</summary>
        bool Loop(Decoder d, WaveOut o)
        {
            int srcChannels = d.Channels;
            var raw = new float[BlockFrames * Math.Max(1, srcChannels)];
            var mixed = new float[BlockFrames * 2];
            var pcm = new short[BlockFrames * 2];
            var attackL = new Ballistics(0.55f, 0.09f);
            var attackR = new Ballistics(0.55f, 0.09f);

            while (!stopping)
            {
                int seekMs = pendingSeekMs;
                if (seekMs >= 0)
                {
                    pendingSeekMs = -1;
                    double target = seekMs / 1000.0;
                    o.Flush();
                    d.Seek(target);
                    equalizer.Reset();
                    seekBase = target;
                    attackL.Reset();
                    attackR.Reset();
                }

                if (paused)
                {
                    // The device is paused and holding its queue; there is nothing
                    // to write and no event to wait on.
                    Thread.Sleep(20);
                    continue;
                }

                int wanted = BlockFrames * srcChannels;
                int got = d.Read(raw, 0, wanted);
                if (got == 0) { DrainQueue(o); return true; }
                if (got < wanted)
                {
                    // Pad the tail so the last partial block is still a whole one
                    // to the device; anything else clicks.
                    Array.Clear(raw, got, wanted - got);
                }
                int frames = wanted / srcChannels;

                Audio.Downmix(raw, srcChannels, mixed, frames);
                equalizer.Process(mixed, frames);
                CaptureLevels(mixed, frames, attackL, attackR);
                Audio.ToPcm(mixed, pcm, frames, volume, balance);

                if (!o.Write(pcm, frames)) return false;

                // The device's own counter is the only honest clock: frames handed
                // over are not frames heard, and the difference is the whole queue
                // depth.
                Interlocked.Exchange(ref positionTicks,
                    (long)((seekBase + o.PlayedSeconds()) * 1000));

                if (got < wanted) { DrainQueue(o); return true; }
            }
            return false;
        }

        /// <summary>
        /// Waits for the device to finish what it has been given. Without it the
        /// last fifth of a second is cut off every track in the playlist.
        /// </summary>
        void DrainQueue(WaveOut o)
        {
            double target = Duration;
            for (int i = 0; i < 80 && !stopping; i++)
            {
                double played = seekBase + o.PlayedSeconds();
                Interlocked.Exchange(ref positionTicks, (long)(played * 1000));
                if (target > 0 && played >= target - 0.02) break;
                if (!o.HasQueuedAudio()) break;
                Thread.Sleep(20);
            }
        }

        void CaptureLevels(float[] mixed, int frames, Ballistics l, Ballistics r)
        {
            // The needles are driven from the signal after the equaliser and
            // before the volume control — a VU meter on a real deck reads the
            // recording, not how loud you are listening to it.
            peakLeft = l.Push(Audio.Peak(mixed, frames, 0));
            peakRight = r.Push(Audio.Peak(mixed, frames, 1));

            lock (levelLock)
            {
                // Slide the window along and drop the newest mono sum in at the
                // end, so the analyser always has a full window even when a block
                // is shorter than one.
                int keep = AnalyserSize - frames;
                if (keep < 0) keep = 0;
                if (keep > 0) Array.Copy(analyser, frames, analyser, 0, keep);
                int start = Math.Max(0, frames - AnalyserSize);
                for (int f = start; f < frames; f++)
                    analyser[keep + (f - start)] = (mixed[f * 2] + mixed[f * 2 + 1]) * 0.5f;
                analyserFill = Math.Min(AnalyserSize, analyserFill + frames);
            }
        }

        public void Dispose()
        {
            Stop();
            opened.Close();
        }
    }
}
