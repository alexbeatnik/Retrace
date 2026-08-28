// The look: neutral dark cards with one accent hue.
//
// Shared with the sister projects next door (../AV/src/Theme.cs,
// ../WinCleaner/src/Theme.cs) so the three read as one suite: navy-tinted
// near-black surfaces, rounded cards with a soft drop shadow and a hairline
// border, Segoe UI throughout, and a single saturated accent carrying every lit
// state. This replaced an amber CRT skin — phosphor text, scanlines over every
// surface, dashed rules, square corners and letter-spaced monospace — which read
// as a prop rather than as something to keep open all day.
//
// The one thing this file keeps from that skin is that the accent is a
// *setting*: a palette is one hue and everything lit is derived from it, so a
// new scheme is a single line in Palettes and the whole app follows. The
// grounds and the three state colours are deliberately not derived — a dark UI
// is dark whatever the accent is, and a warning has to mean the same thing in
// every scheme.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Retrace
{
    /// <summary>
    /// One colour scheme: the accent hue, and the three tones derived from it.
    /// Keeping the derivation here is what holds a new palette to one line and
    /// stops the schemes drifting apart as the UI grows.
    /// </summary>
    sealed class Palette
    {
        public readonly string Id;
        public readonly string Caption;
        public readonly Color Accent;    // the hue: a filled block, a lit control
        public readonly Color Hot;       // the same thing under the pointer
        public readonly Color Soft;      // a translucent wash of it behind a row
        public readonly Color OnAccent;  // text laid on a filled block of Accent

        Palette(string id, string caption, Color hue)
        {
            Id = id;
            Caption = caption;
            Accent = hue;
            Hot = Mix(hue, Color.White, 0.30f);
            // 30/255 alpha rather than a solid tint: the wash goes over a card, a
            // list row and the page ground, and a colour mixed against one of
            // those reads as a patch on the other two.
            Soft = Color.FromArgb(30, hue);
            // White on anything saturated enough to be an accent, near-black on
            // the pale ones — decided by the hue rather than picked per scheme,
            // so a new palette cannot arrive with unreadable labels on it.
            OnAccent = Luma(hue) > 165 ? Color.FromArgb(16, 18, 24) : Color.FromArgb(255, 255, 255);
        }

        internal static double Luma(Color c)
        {
            return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        }

        static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                Clamp(a.R + (b.R - a.R) * t),
                Clamp(a.G + (b.G - a.G) * t),
                Clamp(a.B + (b.B - a.B) * t));
        }

        static int Clamp(float v) { return v < 0 ? 0 : (v > 255 ? 255 : (int)v); }

        // Blue first: it is what the two tools next door wear, and this player is
        // meant to sit beside them.
        public static readonly Palette[] All =
        {
            new Palette("blue",   "Blue",   Color.FromArgb(66, 133, 255)),
            new Palette("teal",   "Teal",   Color.FromArgb(34, 197, 194)),
            new Palette("green",  "Green",  Color.FromArgb(48, 199, 110)),
            new Palette("violet", "Violet", Color.FromArgb(167, 124, 255)),
            new Palette("amber",  "Amber",  Color.FromArgb(245, 171, 53)),
            new Palette("rose",   "Rose",   Color.FromArgb(244, 102, 133))
        };

        public static Palette ById(string id)
        {
            foreach (Palette p in All)
                if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) return p;
            return All[0];
        }
    }

    static class Theme
    {
        // The grounds do not change with the scheme.
        public static readonly Color Bg = Color.FromArgb(16, 18, 24);        // window
        public static readonly Color Card = Color.FromArgb(30, 33, 42);      // cards
        public static readonly Color CardLine = Color.FromArgb(48, 52, 64);  // hairline border
        public static readonly Color Sunken = Color.FromArgb(12, 13, 18);    // lists, wells
        // A button sitting ON a card: filled with Card it vanishes into it and
        // reads as a label, so it gets its own tone.
        public static readonly Color Subtle = Color.FromArgb(44, 48, 60);
        public static readonly Color SubtleHot = Color.FromArgb(58, 63, 78);
        public static readonly Color Text = Color.FromArgb(232, 234, 240);
        public static readonly Color Muted = Color.FromArgb(148, 155, 170);
        public static readonly Color Disabled = Color.FromArgb(92, 97, 108); // grey, not just faded
        public static readonly Color Btn = Color.FromArgb(216, 219, 226);    // light buttons
        public static readonly Color BtnHot = Color.FromArgb(233, 235, 240);
        public static readonly Color BtnText = Color.FromArgb(51, 54, 62);

        // Reserved regardless of scheme: a warning has to mean the same thing in
        // every palette, so these are never derived from the accent.
        public static readonly Color Good = Color.FromArgb(48, 199, 110);
        public static readonly Color Warn = Color.FromArgb(232, 197, 71);
        public static readonly Color Danger = Color.FromArgb(239, 68, 68);

        public const int Radius = 12;      // cards
        public const int RadiusSmall = 6;  // buttons, keys, meter cells

        static Palette current = Palette.All[0];

        /// <summary>Raised when the scheme changes, so every open window can
        /// repaint itself.</summary>
        public static event EventHandler Changed;

        public static Palette Current { get { return current; } }

        public static void Use(string id)
        {
            Palette next = Palette.ById(id);
            if (next == current) return;
            current = next;
            EventHandler h = Changed;
            if (h != null) h(null, EventArgs.Empty);
        }

        public static Color Accent { get { return current.Accent; } }
        public static Color AccentHot { get { return current.Hot; } }
        public static Color AccentSoft { get { return current.Soft; } }
        public static Color OnAccent { get { return current.OnAccent; } }

        // ---- Type ---------------------------------------------------------------

        // Segoe UI on any Windows this runs on; the rest is insurance.
        static readonly string[] UiStack =
            { "Segoe UI", "Segoe UI Variable Text", "Tahoma", "Arial" };
        // A second stack for the readouts that count: a clock and a duration
        // column re-measure on every frame, and a proportional face makes the
        // digits jitter sideways as they change.
        static readonly string[] MonoStack =
            { "Cascadia Mono", "Consolas", "Segoe UI", "Courier New" };
        static string family, mono;

        public static string Family { get { return family ?? (family = Pick(UiStack, "Arial")); } }
        public static string MonoFamily { get { return mono ?? (mono = Pick(MonoStack, "Courier New")); } }

        static string Pick(string[] stack, string fallback)
        {
            foreach (string name in stack)
            {
                try
                {
                    // The FontFamily constructor is the probe: it throws for a
                    // missing family rather than silently substituting, which is
                    // what the Font one would do.
                    using (var probe = new FontFamily(name)) return probe.Name;
                }
                catch (ArgumentException) { }
            }
            return fallback;   // on every Windows since 3.1
        }

        public static Font Ui(float size) { return new Font(Family, size); }
        public static Font UiBold(float size) { return new Font(Family, size, FontStyle.Bold); }
        public static Font UiPx(float px) { return new Font(Family, px, GraphicsUnit.Pixel); }
        public static Font UiBoldPx(float px)
        {
            return new Font(Family, px, FontStyle.Bold, GraphicsUnit.Pixel);
        }
        /// <summary>The face the clocks and the duration column are set in.</summary>
        public static Font Digits(float size) { return new Font(MonoFamily, size); }
        public static Font DigitsBold(float size)
        {
            return new Font(MonoFamily, size, FontStyle.Bold);
        }

        // ---- Geometry -------------------------------------------------------------

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (d <= 0 || r.Width <= d || r.Height <= d) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>A rounded fill, which is most of what every control here
        /// draws. Kept in one place so the antialiasing and the degenerate cases
        /// are handled the same way everywhere.</summary>
        public static void Fill(Graphics g, RectangleF r, float rad, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode was = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(r, rad))
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
            g.SmoothingMode = was;
        }

        public static void Outline(Graphics g, RectangleF r, float rad, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode was = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(r, rad))
            using (var pen = new Pen(c)) g.DrawPath(pen, p);
            g.SmoothingMode = was;
        }

        // ---- Surfaces ---------------------------------------------------------------

        /// <summary>Clears a control to a flat colour. Kept as a named call rather
        /// than inlined into every OnPaint because this is where the scanline
        /// overlay used to be laid over the fill, and it is the seam to reach for
        /// if the surfaces ever want texture again.</summary>
        public static void Surface(Control c, Graphics g, Color back)
        {
            g.Clear(back);
        }

        /// <summary>
        /// An elevated card: a soft drop shadow under the panel, the fill, a
        /// hairline border and a 1px top highlight — depth on a dark ground.
        /// Drawn inside the control's own bounds, leaving room for the shadow.
        /// </summary>
        public static void PaintCard(Graphics g, int w, int h)
        {
            var r = new RectangleF(1.5f, 0.5f, w - 4, h - 6);
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode was = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 1; i <= 4; i++)
                using (var path = Round(new RectangleF(r.X, r.Y + i, r.Width, r.Height), Radius))
                using (var b = new SolidBrush(Color.FromArgb(13, 0, 0, 0)))
                    g.FillPath(b, path);
            using (var path = Round(r, Radius))
            {
                using (var b = new SolidBrush(Card)) g.FillPath(b, path);
                using (var pen = new Pen(CardLine)) g.DrawPath(pen, path);
            }
            using (var hl = new Pen(Color.FromArgb(16, 255, 255, 255)))
                g.DrawLine(hl, r.X + Radius, r.Y + 1, r.Right - Radius, r.Y + 1);
            g.SmoothingMode = was;
        }

        // ---- Labels -----------------------------------------------------------------
        //
        // These wrapped a hand-rolled letter-spacing routine that stepped by one
        // monospace advance per glyph — correct for the terminal face, nonsense
        // for a proportional one — so they are now thin wrappers that keep the
        // call sites, and the auto-sizing controls that have to agree with what
        // actually gets painted, reading the same.

        const TextFormatFlags Plain = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        public static int Measure(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return TextRenderer.MeasureText(s, f, Size.Empty, Plain).Width;
        }

        public static void DrawLabel(Graphics g, string s, Font f, Point at, Color c)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextRenderer.DrawText(g, s, f, at, c, Plain);
        }

        /// <summary>Left-aligned text, vertically centred and ellipsised to its
        /// box.</summary>
        public static void Draw(Graphics g, string s, Font f, Rectangle r, Color c)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextRenderer.DrawText(g, s, f, r, c, TextFormatFlags.Left
                | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        public static void DrawCentered(Graphics g, string s, Font f, Rectangle r, Color c)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextRenderer.DrawText(g, s, f, r, c, TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix);
        }

        public static void DrawRight(Graphics g, string s, Font f, Rectangle r, Color c)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextRenderer.DrawText(g, s, f, r, c, TextFormatFlags.Right
                | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        // ---- Window chrome ----------------------------------------------------------

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // GDI COLORREF is 0x00BBGGRR — not the ARGB order Color uses.
        static int ToColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        public static void DarkTitleBar(Form f) { DarkTitleBar(f, false); }

        /// <summary>
        /// Dark window chrome (Win10 1903+). On Windows 11 the caption is also
        /// painted in the app's own background colour so the title bar does not
        /// read as a foreign grey strip; those attributes fail harmlessly on
        /// Windows 10 and the dark caption survives.
        ///
        /// <paramref name="hideCaptionText"/> paints the caption text in the
        /// caption colour, making it invisible while Form.Text keeps naming the
        /// window for the taskbar, Alt+Tab and screen readers — used by the main
        /// window, whose in-window header already carries the branding.
        /// </summary>
        public static void DarkTitleBar(Form f, bool hideCaptionText)
        {
            EventHandler apply = delegate
            {
                try
                {
                    int on = 1;
                    if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0)
                        DwmSetWindowAttribute(f.Handle, 19, ref on, 4);   // older Win10
                    int caption = ToColorRef(Bg);
                    DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);  // DWMWA_CAPTION_COLOR
                    int text = ToColorRef(hideCaptionText ? Bg : Text);
                    DwmSetWindowAttribute(f.Handle, 36, ref text, 4);     // DWMWA_TEXT_COLOR
                    int border = ToColorRef(CardLine);
                    DwmSetWindowAttribute(f.Handle, 34, ref border, 4);   // DWMWA_BORDER_COLOR
                }
                catch (EntryPointNotFoundException) { }
                catch (DllNotFoundException) { }
            };
            if (f.IsHandleCreated) apply(null, EventArgs.Empty);
            else f.HandleCreated += apply;
        }
    }
}
