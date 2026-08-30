// The output stage: waveOut, the oldest and most universally present audio API
// in Windows. A ring of four buffers is kept queued at the device; the writer
// blocks on an event until the driver hands one back, which is what paces the
// decode thread to real time without a single sleep.
//
// 16-bit PCM rather than float: every driver and every virtual device accepts
// it, whereas IEEE-float output is refused by some. The conversion is one
// multiply per sample in the mixer, which already has the samples in registers.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Retrace
{
    [StructLayout(LayoutKind.Sequential)]
    struct WaveFormatEx
    {
        public short wFormatTag, nChannels;
        public int nSamplesPerSec, nAvgBytesPerSec;
        public short nBlockAlign, wBitsPerSample, cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WaveHdr
    {
        public IntPtr lpData;
        public int dwBufferLength, dwBytesRecorded;
        public IntPtr dwUser;
        public int dwFlags, dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MmTime
    {
        public int wType;
        public int units;  // the union member for TIME_SAMPLES / TIME_BYTES
        public int pad;
    }

    sealed class WaveOut : IDisposable
    {
        const int WAVE_MAPPER = -1;
        const int CALLBACK_EVENT = 0x50000;
        const int WHDR_DONE = 0x1;
        const int WAVE_FORMAT_PCM = 1;

        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, int device,
            ref WaveFormatEx fmt, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutPause(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutRestart(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutGetPosition(IntPtr h, ref MmTime t, int size);

        const int TIME_SAMPLES = 0x2;

        readonly int blockFrames, blockBytes, blocks;
        readonly AutoResetEvent ready = new AutoResetEvent(false);
        readonly IntPtr[] headers;
        readonly IntPtr[] data;
        readonly int hdrSize = Marshal.SizeOf(typeof(WaveHdr));
        readonly object gate = new object();
        IntPtr device;
        int next;          // the block the writer will claim next
        bool disposed;

        public int Channels { get; private set; }
        public int SampleRate { get; private set; }
        /// <summary>Frames the device holds when its queue is full — the latency
        /// between a sample being written and being heard.</summary>
        public int BufferedFrames { get { return blockFrames * blocks; } }

        public WaveOut(int sampleRate, int channels, int blockFrames, int blocks)
        {
            SampleRate = sampleRate;
            Channels = channels;
            this.blockFrames = blockFrames;
            this.blocks = blocks;
            blockBytes = blockFrames * channels * 2;

            var fmt = new WaveFormatEx();
            fmt.wFormatTag = WAVE_FORMAT_PCM;
            fmt.nChannels = (short)channels;
            fmt.nSamplesPerSec = sampleRate;
            fmt.wBitsPerSample = 16;
            fmt.nBlockAlign = (short)(channels * 2);
            fmt.nAvgBytesPerSec = sampleRate * fmt.nBlockAlign;
            fmt.cbSize = 0;

            int hr = waveOutOpen(out device, WAVE_MAPPER, ref fmt,
                ready.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
            if (hr != 0) throw new InvalidOperationException("waveOutOpen failed: " + hr);

            headers = new IntPtr[blocks];
            data = new IntPtr[blocks];
            for (int i = 0; i < blocks; i++)
            {
                data[i] = Marshal.AllocHGlobal(blockBytes);
                headers[i] = Marshal.AllocHGlobal(hdrSize);
                var hdr = new WaveHdr();
                hdr.lpData = data[i];
                hdr.dwBufferLength = blockBytes;
                Marshal.StructureToPtr(hdr, headers[i], false);
                waveOutPrepareHeader(device, headers[i], hdrSize);
                // Every block starts life marked done, so the first four writes
                // can fill the queue without waiting on a device that has not
                // been given anything to play yet.
                MarkDone(i);
            }
        }

        // Both of these read and write memory that Dispose frees; every caller
        // holds `gate` and has checked `disposed`.
        void MarkDone(int i)
        {
            var hdr = (WaveHdr)Marshal.PtrToStructure(headers[i], typeof(WaveHdr));
            hdr.dwFlags |= WHDR_DONE;
            Marshal.StructureToPtr(hdr, headers[i], false);
        }

        bool IsDone(int i)
        {
            var hdr = (WaveHdr)Marshal.PtrToStructure(headers[i], typeof(WaveHdr));
            return (hdr.dwFlags & WHDR_DONE) != 0;
        }

        /// <summary>
        /// Hands one block of interleaved 16-bit frames to the device, waiting for
        /// a free block if the queue is full. Returns false when the wait was
        /// abandoned — the device was reset or closed under it — which is the
        /// signal for the writing thread to stop.
        /// </summary>
        public bool Write(short[] pcm, int frames)
        {
            if (frames <= 0) return true;
            if (frames > blockFrames) frames = blockFrames;
            int samples = frames * Channels;

            // A bounded wait rather than an infinite one: waveOutReset can retire
            // the queued blocks without the event ever being set, and an infinite
            // wait here would strand the decode thread on shutdown.
            int spins = 0;
            while (true)
            {
                // Every read and write of a header or a buffer happens under the
                // gate and behind the disposed flag, because the engine gives up
                // on a wedged writer after a second and a half and then frees
                // them: a check outside the lock is a promise this thread cannot
                // keep, and the memory is gone by the time it copies into it.
                lock (gate)
                {
                    if (disposed) return false;
                    if (IsDone(next))
                    {
                        Marshal.Copy(pcm, 0, data[next], samples);
                        var hdr = (WaveHdr)Marshal.PtrToStructure(headers[next], typeof(WaveHdr));
                        hdr.dwFlags &= ~WHDR_DONE;
                        hdr.dwBufferLength = samples * 2;
                        Marshal.StructureToPtr(hdr, headers[next], false);
                        if (waveOutWrite(device, headers[next], hdrSize) != 0)
                        {
                            MarkDone(next);
                            return false;
                        }
                        next = (next + 1) % blocks;
                        return true;
                    }
                }
                // Closed under us while waiting is the same answer as a reset:
                // the writer is finished either way.
                try { ready.WaitOne(50); }
                catch (ObjectDisposedException) { return false; }
                if (++spins > 200) return false;
            }
        }

        /// <summary>
        /// Seconds of audio the device has actually played since it was opened or
        /// last flushed. This is the only honest playback clock: frames handed to
        /// waveOutWrite sit in the queue for the whole of its depth before they
        /// are heard, so counting what was written runs a fifth of a second ahead
        /// of the music.
        /// </summary>
        public double PlayedSeconds()
        {
            var t = new MmTime();
            t.wType = TIME_SAMPLES;
            lock (gate)
            {
                if (disposed) return 0;
                if (waveOutGetPosition(device, ref t, Marshal.SizeOf(typeof(MmTime))) != 0) return 0;
            }
            // A driver is entitled to answer in a different unit than the one asked
            // for; treating a byte count as a frame count would run the clock four
            // times fast, so an unexpected wType is reported as no information.
            if (t.wType != TIME_SAMPLES) return 0;
            // waveOutGetPosition returns an unsigned 32-bit count. At 48 kHz it
            // wraps after about 25 hours of continuous play, and a signed read
            // would go negative after half of that.
            return (uint)t.units / (double)SampleRate;
        }

        /// <summary>Whether any block is still queued at the device — what tells
        /// the end of a track from the end of its audio.</summary>
        public bool HasQueuedAudio()
        {
            lock (gate)
            {
                if (disposed) return false;
                for (int i = 0; i < blocks; i++) if (!IsDone(i)) return true;
            }
            return false;
        }

        public void Pause()
        {
            lock (gate) { if (!disposed) waveOutPause(device); }
        }

        public void Resume()
        {
            lock (gate) { if (!disposed) waveOutRestart(device); }
        }

        /// <summary>
        /// Drops everything queued and marks the ring free again. Called before a
        /// seek, so the buffered second of the old position is not heard first.
        /// </summary>
        public void Flush()
        {
            lock (gate)
            {
                if (disposed) return;
                waveOutReset(device);
                for (int i = 0; i < blocks; i++) MarkDone(i);
                next = 0;
            }
            // waveOutReset retires the blocks without setting the event; wake the
            // writer by hand or it sits out its full timeout for nothing.
            ready.Set();
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                waveOutReset(device);
            }
            ready.Set();
            // The driver may still be inside a callback for a block it has just
            // retired; unpreparing a header it has not finished with is what
            // corrupts the heap on close.
            Thread.Sleep(30);
            // Under the gate as well: an abandoned writer that is still running
            // takes it for every touch of these, and freeing them alongside it
            // rather than under it is the access violation this all guards.
            lock (gate)
            {
                for (int i = 0; i < blocks; i++)
                {
                    waveOutUnprepareHeader(device, headers[i], hdrSize);
                    Marshal.FreeHGlobal(headers[i]);
                    Marshal.FreeHGlobal(data[i]);
                }
                waveOutClose(device);
                device = IntPtr.Zero;
            }
            ready.Close();
        }
    }
}
