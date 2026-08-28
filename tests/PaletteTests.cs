// The colour schemes. A palette is one accent hue and the tones derived from
// it, so these check the derivation rather than any particular colour — a new
// scheme is one line, and it has to come out usable without being hand-checked.
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Retrace.Tests
{
    public static class PaletteTests
    {
        public static void TestBlueIsTheDefault()
        {
            // The player is meant to sit beside the two tools next door, which
            // are blue.
            Assert.Equal("blue", Palette.All[0].Id, "the first scheme");
            Assert.Equal("blue", Palette.ById("nonsense").Id, "an unknown id falls back to it");
            // A settings file written by the amber build names a scheme that no
            // longer exists under that hue; falling back rather than throwing is
            // what lets an old file still open the player.
            Assert.Equal("blue", Palette.ById("ice").Id, "a scheme that has since gone");
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

        public static void TestHotIsLighterThanTheAccent()
        {
            // Hover has to read as brighter than rest in every scheme, or the
            // controls stop answering the pointer.
            foreach (Palette p in Palette.All)
                Assert.True(Luma(p.Hot) > Luma(p.Accent), p.Id + ": hot is not lighter");
        }

        public static void TestTheSoftWashIsTranslucent()
        {
            // The wash goes over a card, a list row and the page ground; mixed
            // against one of them it would read as a patch on the other two.
            foreach (Palette p in Palette.All)
            {
                Assert.True(p.Soft.A > 0 && p.Soft.A < 90, p.Id + ": the wash is not a wash");
                Assert.Equal(p.Accent.R, p.Soft.R, p.Id + ": the wash is not the accent hue");
            }
        }

        public static void TestAccentStandsOffTheCard()
        {
            // Every scheme's accent is used as text on a card — a lit tab, a
            // playing row, the clock's own hue — so it has to be legible there.
            foreach (Palette p in Palette.All)
                Assert.True(Luma(p.Accent) - Luma(Theme.Card) > 60,
                    p.Id + ": the accent does not stand off the card");
        }

        public static void TestOnAccentIsReadableOnTheFill()
        {
            // A nav tab and a primary button fill with the accent and print
            // OnAccent on top; that pair has to stay legible in every scheme, in
            // whichever direction the derivation went.
            foreach (Palette p in Palette.All)
                Assert.True(Math.Abs(Luma(p.Accent) - Luma(p.OnAccent)) > 80,
                    p.Id + ": text on the accent fill has too little contrast");
        }

        public static void TestTheFixedTonesStayOrdered()
        {
            // Text over muted over disabled over the border is the hierarchy the
            // whole UI leans on, and none of it follows the scheme.
            Assert.True(Luma(Theme.Text) > Luma(Theme.Muted), "muted is not darker than text");
            Assert.True(Luma(Theme.Muted) > Luma(Theme.Disabled), "disabled is not darker than muted");
            Assert.True(Luma(Theme.Disabled) > Luma(Theme.CardLine), "the rule is not the darkest");
            Assert.True(Luma(Theme.Card) > Luma(Theme.Bg), "a card does not lift off the page");
            Assert.True(Luma(Theme.Bg) > Luma(Theme.Sunken), "a well does not sink into the page");
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
        /// enough for ordering two tones against each other.</summary>
        static double Luma(Color c)
        {
            return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        }
    }
}
