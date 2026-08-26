// The colour schemes. Everything but the near-black grounds is derived from one
// base hue, so these check the derivation rather than any particular colour —
// a new palette is one line, and it has to come out usable without being
// hand-checked.
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Retrace.Tests
{
    public static class PaletteTests
    {
        public static void TestAmberIsTheDefault()
        {
            // The player is meant to sit beside WindowsStalker, which is amber.
            Assert.Equal("amber", Palette.All[0].Id, "the first scheme");
            Assert.Equal("amber", Palette.ById("nonsense").Id, "an unknown id falls back to it");
        }

        public static void TestEveryIdIsUniqueAndResolvable()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Palette p in Palette.All)
            {
                Assert.True(seen.Add(p.Id), "duplicate scheme id: " + p.Id);
                Assert.Equal(p.Id, Palette.ById(p.Id).Id, "round trip for " + p.Id);
                Assert.True(p.Caption.Trim().Length > 0, p.Id + " has no caption");
            }
        }

        public static void TestIdLookupIgnoresCase()
        {
            // The id comes back from settings.ini, which a person may have edited.
            Assert.Equal("green", Palette.ById("GREEN").Id, "upper case");
            Assert.Equal("green", Palette.ById("Green").Id, "mixed case");
        }

        public static void TestDerivedTonesAreOrdered()
        {
            // Bright must read as lighter than Text, and Muted and Line as
            // progressively darker, or the hierarchy the whole UI leans on
            // inverts and captions come out louder than headings.
            foreach (Palette p in Palette.All)
            {
                double bright = Luma(p.Bright), text = Luma(p.Text);
                double muted = Luma(p.Muted), line = Luma(p.Line);
                Assert.True(bright > text, p.Id + ": bright is not lighter than text");
                Assert.True(text > muted, p.Id + ": muted is not darker than text");
                Assert.True(muted > line, p.Id + ": line is not darker than muted");
            }
        }

        public static void TestTextIsReadableOnTheCard()
        {
            // Every scheme's phosphor has to stand off the near-black card, or the
            // app is unreadable in it.
            foreach (Palette p in Palette.All)
                Assert.True(Luma(p.Text) - Luma(Theme.Card) > 60,
                    p.Id + ": text does not stand off the card");
        }

        public static void TestOnAccentIsReadableOnTheFill()
        {
            // A nav tab and a primary button fill with Text and print OnAccent on
            // top; that pair has to stay legible in every scheme.
            foreach (Palette p in Palette.All)
                Assert.True(Luma(p.Text) - Luma(p.OnAccent) > 80,
                    p.Id + ": text on the accent fill has too little contrast");
        }

        public static void TestUseSwitchesAndAnnounces()
        {
            string was = Theme.Current.Id;
            int fired = 0;
            EventHandler count = delegate { fired++; };
            Theme.Changed += count;
            try
            {
                Theme.Use("green");
                Assert.Equal("green", Theme.Current.Id, "the scheme changed");
                Assert.Equal(1, fired, "and said so once");

                // Selecting the scheme already in use must not churn every control
                // in the window for nothing.
                Theme.Use("green");
                Assert.Equal(1, fired, "re-selecting the same scheme is silent");
            }
            finally
            {
                Theme.Changed -= count;
                Theme.Use(was);
            }
        }

        /// <summary>Perceived brightness — the usual weighting, which is close
        /// enough for ordering two tones of the same hue.</summary>
        static double Luma(Color c)
        {
            return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        }
    }
}
