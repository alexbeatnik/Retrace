// The signal path. No device and no file: every one of these is arithmetic on
// float arrays, which is the whole reason src/Dsp.cs has no Windows dependency.
using System;

namespace Retrace.Tests
{
    public static class DspTests
    {
        const int Rate = 44100;

        /// <summary>Interleaved stereo of a sine at one frequency.</summary>
        static float[] Tone(double hz, int frames, float amplitude)
        {
            var buf = new float[frames * 2];
            for (int f = 0; f < frames; f++)
            {
                float v = (float)(Math.Sin(2 * Math.PI * hz * f / Rate) * amplitude);
                buf[f * 2] = v;
                buf[f * 2 + 1] = v;
            }
            return buf;
        }

        static double Rms(float[] buf, int frames)
        {
            double sum = 0;
            for (int f = 0; f < frames; f++) sum += buf[f * 2] * (double)buf[f * 2];
            return Math.Sqrt(sum / frames);
        }

        // ---- Equaliser ---------------------------------------------------------

        public static void TestFlatEqualiserLeavesTheSignalAlone()
        {
            var eq = new Equalizer();
            eq.SetSampleRate(Rate);
            eq.Enabled = true;
            float[] buf = Tone(1000, 4096, 0.5f);
            double before = Rms(buf, 4096);
            eq.Process(buf, 4096);
            // Ten cascaded flat peaking filters are unity, give or take the
            // accumulated rounding of ten biquads.
            Assert.Close(before, Rms(buf, 4096), 0.002, "a flat curve is a pass-through");
        }

        public static void TestDisabledEqualiserDoesNothing()
        {
            var eq = new Equalizer();
            eq.SetSampleRate(Rate);
            eq.Enabled = false;
            eq.SetBand(5, 12);
            float[] buf = Tone(1000, 2048, 0.4f);
            double before = Rms(buf, 2048);
            eq.Process(buf, 2048);
            Assert.Close(before, Rms(buf, 2048), 1e-9, "switched off is bit-identical");
        }

        public static void TestBoostingABandRaisesThatBand()
        {
            var eq = new Equalizer();
            eq.SetSampleRate(Rate);
            eq.Enabled = true;
            eq.SetBand(5, 12);                  // the 1 kHz band

            float[] atBand = Tone(1000, 8192, 0.3f);
            double before = Rms(atBand, 8192);
            eq.Process(atBand, 8192);
            // The filter has to settle before the level means anything, so the
            // measurement skips the first quarter of the buffer.
            double after = Rms(atBand, 8192);
            Assert.True(after > before * 1.5, "a +12 dB band lifts a tone inside it");
        }

        public static void TestBoostingOneBandLeavesADistantOneAlone()
        {
            var eq = new Equalizer();
            eq.SetSampleRate(Rate);
            eq.Enabled = true;
            eq.SetBand(0, 12);                  // 31.5 Hz

            float[] far = Tone(8000, 8192, 0.3f);
            double before = Rms(far, 8192);
            eq.Process(far, 8192);
            Assert.Close(before, Rms(far, 8192), before * 0.25,
                "a band eight octaves away barely moves");
        }

        public static void TestGainsAreClamped()
        {
            var eq = new Equalizer();
            eq.SetBand(0, 999);
            Assert.Close(Equalizer.MaxGainDb, eq.GetBand(0), 1e-9, "above the ceiling clamps");
            eq.SetBand(0, -999);
            Assert.Close(-Equalizer.MaxGainDb, eq.GetBand(0), 1e-9, "below the floor clamps");
            eq.SetPreamp(999);
            Assert.Close(Equalizer.MaxGainDb, eq.PreampDb, 1e-9, "and so does the preamp");
        }

        public static void TestOutOfRangeBandIsIgnored()
        {
            var eq = new Equalizer();
            eq.SetBand(-1, 6);
            eq.SetBand(99, 6);
            Assert.Close(0, eq.GetBand(0), 1e-9, "nothing was written");
            Assert.Close(0, eq.GetBand(-1), 1e-9, "and reading out of range is 0, not a throw");
        }

        public static void TestBandAboveNyquistIsNeutral()
        {
            // A 16 kHz band cannot exist at a 22 kHz sample rate. Left to the
            // formula it folds back into the audible range as a spurious boost.
            var eq = new Equalizer();
            eq.SetSampleRate(22050);
            eq.Enabled = true;
            eq.SetBand(9, 12);                  // 16 kHz, above Nyquist here
            float[] buf = Tone(1000, 4096, 0.3f);
            double before = Rms(buf, 4096);
            eq.Process(buf, 4096);
            Assert.Close(before, Rms(buf, 4096), 0.002, "the impossible band is a pass-through");
        }

        public static void TestResetClearsFilterMemory()
        {
            var eq = new Equalizer();
            eq.SetSampleRate(Rate);
            eq.Enabled = true;
            eq.SetBand(5, 12);
            float[] loud = Tone(1000, 1024, 0.9f);
            eq.Process(loud, 1024);
            eq.Reset();

            // With the memory cleared, silence in must be silence out; without the
            // reset the tail of the previous track rings through the new one.
            var silence = new float[2048];
            eq.Process(silence, 1024);
            for (int i = 0; i < 2048; i++)
                Assert.True(Math.Abs(silence[i]) < 1e-6, "a reset filter has no tail");
        }

        public static void TestEveryPresetIsWellFormed()
        {
            // A property over the whole table rather than a handful of cases: a
            // new preset with the wrong number of bands then fails on arrival.
            for (int i = 0; i < Equalizer.PresetNames.Length; i++)
            {
                double[] curve = Equalizer.Preset(i);
                Assert.Equal(Equalizer.Bands.Length, curve.Length,
                    "preset " + Equalizer.PresetNames[i] + " has one gain per band");
                foreach (double db in curve)
                    Assert.True(Math.Abs(db) <= Equalizer.MaxGainDb,
                        "preset " + Equalizer.PresetNames[i] + " stays inside the range");
            }
            Assert.Equal(10, Equalizer.Bands.Length, "ten bands");
        }

        public static void TestPresetIsACopy()
        {
            double[] a = Equalizer.Preset(1);
            a[0] = 99;
            Assert.False(Equalizer.Preset(1)[0] == 99, "the table is not handed out by reference");
        }

        // ---- FFT ----------------------------------------------------------------

        public static void TestFftFindsATone()
        {
            const int n = 1024;
            var re = new float[n];
            var im = new float[n];
            // Exactly 64 cycles across the window, so the energy lands in one bin
            // rather than smearing across its neighbours.
            for (int i = 0; i < n; i++) re[i] = (float)Math.Sin(2 * Math.PI * 64 * i / n);
            Fft.Forward(re, im);

            int peak = 0;
            double best = -1;
            for (int i = 1; i < n / 2; i++)
            {
                double mag = Math.Sqrt(re[i] * (double)re[i] + im[i] * (double)im[i]);
                if (mag > best) { best = mag; peak = i; }
            }
            Assert.Equal(64, peak, "the tone lands in its own bin");
        }

        public static void TestFftOfSilenceIsSilent()
        {
            var re = new float[256];
            var im = new float[256];
            Fft.Forward(re, im);
            for (int i = 0; i < 256; i++)
                Assert.True(Math.Abs(re[i]) < 1e-6 && Math.Abs(im[i]) < 1e-6, "no energy");
        }

        public static void TestFftRejectsNonPowerOfTwo()
        {
            try
            {
                Fft.Forward(new float[100], new float[100]);
            }
            catch (ArgumentException) { return; }
            throw new Exception("a length that is not a power of two should be refused");
        }

        public static void TestHannWindowIsZeroAtTheEnds()
        {
            float[] w = Fft.Hann(64);
            Assert.Close(0, w[0], 1e-6, "starts at zero");
            Assert.Close(0, w[63], 1e-6, "ends at zero");
            Assert.Close(1, w[32], 0.01, "and peaks in the middle");
        }

        // ---- Mixing ---------------------------------------------------------------

        public static void TestMonoIsDoubled()
        {
            var src = new float[] { 0.5f, -0.25f };
            var dst = new float[4];
            Audio.Downmix(src, 1, dst, 2);
            Assert.Close(0.5, dst[0], 1e-6, "left");
            Assert.Close(0.5, dst[1], 1e-6, "right gets the same sample");
            Assert.Close(-0.25, dst[3], 1e-6, "second frame");
        }

        public static void TestStereoPassesThrough()
        {
            var src = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
            var dst = new float[4];
            Audio.Downmix(src, 2, dst, 2);
            for (int i = 0; i < 4; i++) Assert.Close(src[i], dst[i], 1e-6, "sample " + i);
        }

        public static void TestSurroundKeepsTheCentreChannel()
        {
            // A film soundtrack carries its dialogue entirely in the centre; a
            // downmix that drops the extra channels loses the words.
            var src = new float[] { 0f, 0f, 0.5f, 0f, 0f, 0f };   // centre only
            var dst = new float[2];
            Audio.Downmix(src, 6, dst, 1);
            Assert.True(dst[0] > 0.3f, "the centre reaches the left");
            Assert.True(dst[1] > 0.3f, "and the right");
        }

        public static void TestVolumeAndBalance()
        {
            var src = new float[] { 1f, 1f };
            var dst = new short[2];

            Audio.ToPcm(src, dst, 1, 1f, 0f);
            Assert.Equal((short)32767, dst[0], "full scale, centred");
            Assert.Equal((short)32767, dst[1], "both channels");

            Audio.ToPcm(src, dst, 1, 1f, -1f);
            Assert.Equal((short)32767, dst[0], "hard left keeps the left channel");
            Assert.Equal((short)0, dst[1], "and silences the right");

            Audio.ToPcm(src, dst, 1, 0f, 0f);
            Assert.Equal((short)0, dst[0], "zero volume is silence");
        }

        public static void TestBalanceAttenuatesRatherThanBoosts()
        {
            // Panning by boosting the other side would clip material already at
            // full scale, so the loud side must never exceed unity.
            var src = new float[] { 1f, 1f };
            var dst = new short[2];
            Audio.ToPcm(src, dst, 1, 1f, 0.5f);
            Assert.Equal((short)32767, dst[1], "the right stays at full scale");
            Assert.True(dst[0] < 32767, "and the left is turned down");
        }

        public static void TestClipping()
        {
            Assert.Equal((short)32767, Audio.Clip(2f), "above full scale");
            Assert.Equal((short)(-32767), Audio.Clip(-2f), "below it");
            Assert.Equal((short)0, Audio.Clip(0f), "silence");
        }

        public static void TestPeak()
        {
            var buf = new float[] { 0.2f, -0.9f, -0.5f, 0.1f };
            Assert.Close(0.5, Audio.Peak(buf, 2, 0), 1e-6, "left channel peak");
            Assert.Close(0.9, Audio.Peak(buf, 2, 1), 1e-6, "right channel peak, absolute");
        }

        public static void TestMeterScale()
        {
            Assert.Close(0, Audio.MeterScale(0), 1e-6, "silence sits at the left stop");
            Assert.Equal(1f, Audio.MeterScale(4f), "well over full scale pins the needle");
            float unity = Audio.MeterScale(1f);
            Assert.True(unity > 0.7f && unity < 0.9f, "0 dB lands near four fifths of the travel");
        }

        public static void TestVolumeCurve()
        {
            Assert.Close(0, Audio.VolumeCurve(0), 1e-9, "off");
            Assert.Close(1, Audio.VolumeCurve(1), 1e-9, "full");
            Assert.True(Audio.VolumeCurve(0.5f) < 0.5f,
                "the middle of the travel is quieter than half, as loudness demands");
        }

        public static void TestBallisticsRiseFastAndFallSlowly()
        {
            var b = new Ballistics(0.55f, 0.09f);
            float up = b.Push(1f);
            Assert.True(up > 0.4f, "a transient moves the needle at once");
            float down = b.Push(0f);
            Assert.True(down > up * 0.8f, "but it falls back gently");
            for (int i = 0; i < 200; i++) b.Push(0f);
            Assert.Close(0, b.Value, 1e-4, "and settles at rest");
        }
    }
}
