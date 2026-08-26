// The signal path between the decoder and the sound card: downmix, a ten-band
// graphic equaliser, level metering and the spectrum analysis behind the
// display. Everything here is plain arithmetic on float arrays with no Windows
// dependency, which is what lets tests/DspTests.cs check it without a device.
using System;

namespace Retrace
{
    /// <summary>
    /// A direct-form-I biquad. One instance filters one channel: the state is the
    /// last two samples in and out, so channels must never share one or the
    /// stereo image collapses into a comb filter.
    /// </summary>
    sealed class Biquad
    {
        double b0 = 1, b1, b2, a1, a2;
        double x1, x2, y1, y2;

        /// <summary>
        /// A peaking band, from the Audio EQ Cookbook. <paramref name="q"/> at
        /// 1.41 is the one-octave width the band centres below are spaced at.
        /// </summary>
        public void SetPeaking(double sampleRate, double freq, double gainDb, double q)
        {
            // Above Nyquist the formula folds the band back down into the audible
            // range as a spurious boost, so a band the file's rate cannot carry is
            // made a pass-through instead. A 16 kHz slider on a 22 kHz-rate file is
            // exactly this case.
            if (freq >= sampleRate * 0.5 || q <= 0)
            {
                b0 = 1; b1 = 0; b2 = 0; a1 = 0; a2 = 0;
                return;
            }
            double a = Math.Pow(10, gainDb / 40.0);
            double w0 = 2 * Math.PI * freq / sampleRate;
            double cos = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2 * q);
            double a0 = 1 + alpha / a;
            b0 = (1 + alpha * a) / a0;
            b1 = (-2 * cos) / a0;
            b2 = (1 - alpha * a) / a0;
            a1 = (-2 * cos) / a0;
            a2 = (1 - alpha / a) / a0;
        }

        public void Reset() { x1 = x2 = y1 = y2 = 0; }

        public float Process(float sample)
        {
            double x = sample;
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x;
            y2 = y1; y1 = y;
            return (float)y;
        }
    }

    /// <summary>
    /// The ten-band graphic equaliser on the front of the rack unit, plus the
    /// preamp ahead of it. Bands are the ISO octave centres a real graphic EQ is
    /// marked with.
    /// </summary>
    sealed class Equalizer
    {
        public static readonly double[] Bands =
            { 31.5, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
        public const double BandQ = 1.41;   // one octave, matching the spacing
        public const double MaxGainDb = 12;

        readonly Biquad[] left = new Biquad[Bands.Length];
        readonly Biquad[] right = new Biquad[Bands.Length];
        readonly double[] gains = new double[Bands.Length];
        double sampleRate = 44100;
        float preampGain = 1;

        public bool Enabled;

        public Equalizer()
        {
            for (int i = 0; i < Bands.Length; i++)
            {
                left[i] = new Biquad();
                right[i] = new Biquad();
            }
        }

        public void SetSampleRate(double rate)
        {
            if (rate <= 0 || rate == sampleRate) return;
            sampleRate = rate;
            for (int i = 0; i < Bands.Length; i++) Recalculate(i);
            Reset();
        }

        public void SetBand(int index, double gainDb)
        {
            if (index < 0 || index >= Bands.Length) return;
            gains[index] = Clamp(gainDb);
            Recalculate(index);
        }

        public double GetBand(int index)
        {
            return index >= 0 && index < Bands.Length ? gains[index] : 0;
        }

        public void SetPreamp(double gainDb)
        {
            preampGain = (float)Math.Pow(10, Clamp(gainDb) / 20.0);
            PreampDb = Clamp(gainDb);
        }

        public double PreampDb { get; private set; }

        static double Clamp(double db)
        {
            if (db > MaxGainDb) return MaxGainDb;
            if (db < -MaxGainDb) return -MaxGainDb;
            return db;
        }

        void Recalculate(int i)
        {
            left[i].SetPeaking(sampleRate, Bands[i], gains[i], BandQ);
            right[i].SetPeaking(sampleRate, Bands[i], gains[i], BandQ);
        }

        /// <summary>Clears the filter memory. Required on a seek or a track
        /// change, or the tail of the old audio rings through the new.</summary>
        public void Reset()
        {
            for (int i = 0; i < Bands.Length; i++) { left[i].Reset(); right[i].Reset(); }
        }

        /// <summary>Filters interleaved stereo in place.</summary>
        public void Process(float[] buffer, int frames)
        {
            if (!Enabled) return;
            for (int f = 0; f < frames; f++)
            {
                int i = f * 2;
                float l = buffer[i] * preampGain;
                float r = buffer[i + 1] * preampGain;
                for (int b = 0; b < Bands.Length; b++)
                {
                    l = left[b].Process(l);
                    r = right[b].Process(r);
                }
                buffer[i] = l;
                buffer[i + 1] = r;
            }
        }

        /// <summary>The named settings printed on the selector of a period EQ.
        /// Order matches <see cref="PresetNames"/>.</summary>
        public static readonly string[] PresetNames =
            { "FLAT", "ROCK", "POP", "JAZZ", "CLASSIC", "DANCE", "VOCAL", "BASS", "TREBLE", "LOUDNESS" };

        static readonly double[][] Presets = new double[][]
        {
            new double[] {  0,  0,  0,  0,  0,  0,  0,  0,  0,  0 }, // flat
            new double[] {  5,  4,  3, -1, -2,  1,  3,  5,  5,  4 }, // rock
            new double[] { -1,  1,  3,  4,  3,  0, -1, -1,  0,  1 }, // pop
            new double[] {  4,  3,  1,  1, -1, -1,  0,  2,  3,  4 }, // jazz
            new double[] {  4,  3,  2,  0,  0,  0, -1,  1,  3,  4 }, // classical
            new double[] {  7,  6,  4,  1,  0, -2, -3, -1,  2,  4 }, // dance
            new double[] { -3, -2,  0,  3,  5,  5,  4,  2,  0, -2 }, // vocal
            new double[] {  9,  8,  6,  3,  1,  0,  0,  0,  0,  0 }, // bass boost
            new double[] {  0,  0,  0,  0,  0,  1,  3,  6,  8,  9 }, // treble boost
            new double[] {  7,  5,  2,  0, -1,  0,  1,  3,  6,  7 }  // loudness
        };

        public static double[] Preset(int index)
        {
            if (index < 0 || index >= Presets.Length) index = 0;
            return (double[])Presets[index].Clone();
        }
    }

    /// <summary>
    /// Radix-2 Cooley-Tukey, in place. Only the forward transform is needed —
    /// nothing here ever goes back to the time domain.
    /// </summary>
    static class Fft
    {
        public static void Forward(float[] re, float[] im)
        {
            int n = re.Length;
            if (n < 2 || (n & (n - 1)) != 0)
                throw new ArgumentException("FFT length must be a power of two");

            // Bit-reversal permutation, computed by carrying an increment through
            // the reversed index rather than reversing each one from scratch.
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    float t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                float wr = (float)Math.Cos(ang), wi = (float)Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    float cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        float tr = re[b] * cr - im[b] * ci;
                        float ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                        float nr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = nr;
                    }
                }
            }
        }

        /// <summary>Hann window, precomputed for a given length.</summary>
        public static float[] Hann(int n)
        {
            var w = new float[n];
            for (int i = 0; i < n; i++)
                w[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)));
            return w;
        }
    }

    /// <summary>
    /// A meter needle. A real VU movement is a mass on a spring: it rises quickly,
    /// falls slowly, and overshoots a little on a transient. Reproducing that is
    /// what separates a meter from a bar chart — the numbers alone jitter.
    /// </summary>
    sealed class Ballistics
    {
        readonly float attack, decay;
        float value;

        public Ballistics(float attack, float decay)
        {
            this.attack = attack;
            this.decay = decay;
        }

        public float Value { get { return value; } }

        public float Push(float target)
        {
            if (target > value) value += (target - value) * attack;
            else value += (target - value) * decay;
            if (value < 0.0001f) value = 0;
            return value;
        }

        public void Reset() { value = 0; }
    }

    static class Audio
    {
        /// <summary>
        /// Folds any channel count down to interleaved stereo. Mono doubles;
        /// stereo passes through; anything wider takes front left and right and
        /// mixes the remaining channels into both at -3 dB, which keeps a 5.1
        /// film soundtrack's dialogue — carried entirely by the centre — audible
        /// instead of dropping it with the channels nobody asked about.
        /// </summary>
        public static void Downmix(float[] src, int srcChannels, float[] dst, int frames)
        {
            if (srcChannels == 2)
            {
                Array.Copy(src, dst, frames * 2);
                return;
            }
            if (srcChannels == 1)
            {
                for (int f = 0; f < frames; f++)
                {
                    float v = src[f];
                    dst[f * 2] = v;
                    dst[f * 2 + 1] = v;
                }
                return;
            }
            const float rest = 0.7071f;
            for (int f = 0; f < frames; f++)
            {
                int s = f * srcChannels;
                float l = src[s], r = src[s + 1];
                float extra = 0;
                for (int c = 2; c < srcChannels; c++) extra += src[s + c];
                extra *= rest;
                dst[f * 2] = l + extra;
                dst[f * 2 + 1] = r + extra;
            }
        }

        /// <summary>
        /// Applies volume and balance, then converts to the 16-bit frames the
        /// device takes. Balance attenuates the channel being turned away from
        /// rather than boosting the other, so a hard pan cannot clip material that
        /// was already at full scale.
        /// </summary>
        public static void ToPcm(float[] src, short[] dst, int frames, float volume, float balance)
        {
            float gl = volume * (balance > 0 ? 1 - balance : 1);
            float gr = volume * (balance < 0 ? 1 + balance : 1);
            for (int f = 0; f < frames; f++)
            {
                dst[f * 2] = Clip(src[f * 2] * gl);
                dst[f * 2 + 1] = Clip(src[f * 2 + 1] * gr);
            }
        }

        /// <summary>
        /// Hard-clips to the 16-bit range. 32767 rather than 32768 on both sides:
        /// the asymmetry of two's complement is inaudible, and rounding the
        /// negative limit to -32768 is how a full-scale sample wraps to a loud
        /// click on a decoder that overshoots slightly.
        /// </summary>
        public static short Clip(float v)
        {
            float s = v * 32767f;
            if (s > 32767f) return 32767;
            if (s < -32767f) return -32767;
            return (short)s;
        }

        /// <summary>Peak absolute value of one channel in an interleaved stereo
        /// buffer, which is what the VU needles are driven from.</summary>
        public static float Peak(float[] buffer, int frames, int channel)
        {
            float peak = 0;
            for (int f = 0; f < frames; f++)
            {
                float v = buffer[f * 2 + channel];
                if (v < 0) v = -v;
                if (v > peak) peak = v;
            }
            return peak;
        }

        /// <summary>
        /// Maps a linear amplitude onto the travel of a VU needle. The scale is
        /// the one printed on the meter: -20 dB at the left stop, 0 dB at about
        /// four fifths, and the red stretch past it.
        /// </summary>
        public static float MeterScale(float amplitude)
        {
            if (amplitude <= 0.0001f) return 0;
            double db = 20 * Math.Log10(amplitude);
            if (db < -20) return 0;
            if (db > 5) return 1;
            return (float)((db + 20) / 25.0);
        }

        /// <summary>
        /// A volume slider that behaves. Loudness follows amplitude roughly as a
        /// cube law, so a linear fader spends most of its travel in a range that
        /// sounds like full volume and dies in the last centimetre.
        /// </summary>
        public static float VolumeCurve(float position)
        {
            if (position <= 0) return 0;
            if (position >= 1) return 1;
            return position * position * position;
        }
    }
}
