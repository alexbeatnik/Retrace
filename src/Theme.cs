// The look: a phosphor terminal rendered as dark cards.
//
// Carried over from WindowsStalker so the two read as the same machine —
// near-black glass, a single phosphor hue, dashed rules, square corners and
// scanlines. The one thing added here is that the hue is a *setting*: a palette
// is one base colour and everything else is derived from it, so a new scheme is
// a single line in Palettes and the whole app follows.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Retrace
{
    /// <summary>
    /// One colour scheme. Everything but the near-black grounds is derived from
    /// a single base hue, which is what keeps a new palette to one line and stops
    /// the schemes drifting apart as the UI grows.
    /// </summary>
    sealed class Palette
    {
        public readonly string Id;
        public readonly string Caption;
        public readonly Color Text;       // the phosphor itself
        public readonly Color Bright;     // headings and the hot state of a control
        public readonly Color Muted;      // captions and secondary readouts
        public readonly Color Line;       // the dashed rule around a card
        public readonly Color Subtle;     // a button sitting on a card
        public readonly Color OnAccent;   // text laid on a filled block of Text

        Palette(string id, string caption, Color baseColour)
        {
            Id = id;
            Caption = caption;
            Text = baseColour;
            Bright = Mix(baseColour, Color.White, 0.42f);
            Muted = Scale(baseColour, 0.62f);
            Line = Scale(baseColour, 0.36f);
            // Warmed towards the hue rather than a neutral grey, or a button on a
            // card reads as a hole punched in it.
            Subtle = Mix(Scale(baseColour, 0.20f), Color.FromArgb(14, 14, 14), 0.55f);
            OnAccent = Scale(baseColour, 0.06f);
        }

        static Color Scale(Color c, float f)
        {
            return Color.FromArgb(Clamp(c.R * f), Clamp(c.G * f), Clamp(c.B * f));
        }

        static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                Clamp(a.R + (b.R - a.R) * t),
                Clamp(a.G + (b.G - a.G) * t),
                Clamp(a.B + (b.B - a.B) * t));
        }

        static int Clamp(float v) { return v < 0 ? 0 : (v > 255 ? 255 : (int)v); }

        // Amber first: it is the scheme WindowsStalker wears, and this player is
        // meant to sit beside it.
        public static readonly Palette[] All =
        {
            new Palette("amber",  "AMBER",   Color.FromArgb(255, 176, 0)),
            new Palette("green",  "GREEN",   Color.FromArgb(64, 232, 96)),
            new Palette("ice",    "ICE",     Color.FromArgb(86, 200, 255)),
            new Palette("violet", "VIOLET",  Color.FromArgb(186, 142, 255)),
            new Palette("ember",  "EMBER",   Color.FromArgb(255, 104, 72)),
            new Palette("bone",   "BONE",    Color.FromArgb(214, 214, 208))
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
        // The grounds do not change with the scheme: a phosphor display is dark
        // whatever colour the phosphor is.
        public static readonly Color Bg = Color.FromArgb(5, 5, 5);
        public static readonly Color Card = Color.FromArgb(10, 10, 10);
        public static readonly Color Sunken = Color.FromArgb(0, 0, 0);
        public static readonly Color Btn = Color.FromArgb(48, 48, 48);
        public static readonly Color BtnHot = Color.FromArgb(66, 66, 66);
        public static readonly Color Disabled = Color.FromArgb(84, 84, 84);

        // Reserved regardless of scheme: a warning has to mean the same thing in
        // every palette, so these are never derived from the base hue.
        public static readonly Color Good = Color.FromArgb(102, 204, 102);
        public static readonly Color Warn = Color.FromArgb(255, 96, 32);
        public static readonly Color Danger = Color.FromArgb(216, 68, 47);

        public const int Radius = 0;      // nothing on a terminal is rounded
        public const float Track = 1.4f;  // letter-spacing, in pixels

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

        public static Color Text { get { return current.Text; } }
        public static Color Bright { get { return current.Bright; } }
        public static Color Muted { get { return current.Muted; } }
        public static Color Line { get { return current.Line; } }
        public static Color Subtle { get { return current.Subtle; } }
        public static Color OnAccent { get { return current.OnAccent; } }

        // ---- Type ---------------------------------------------------------------

        // The terminal monospace stack, first one installed wins. Mono glyphs run
        // wider than a UI face at the same point size and this is a fixed layout
        // of hand-placed parts, so the compensation is one knob here rather than a
        // fudged number at each call site.
        const float FontScale = 0.88f;
        static readonly string[] MonoStack =
            { "Cascadia Mono", "Consolas", "DejaVu Sans Mono", "Courier New" };
        static string family;

        public static string Family
        {
            get
            {
                if (family == null)
                {
                    family = "Courier New";   // on every Windows since 3.1
                    foreach (string name in MonoStack)
                    {
                        try
                        {
                            // The FontFamily constructor is the probe: it throws
                            // for a missing family rather than silently
                            // substituting, which is what the Font one would do.
                            using (var probe = new FontFamily(name)) { family = probe.Name; break; }
                        }
                        catch (ArgumentException) { }
                    }
                }
                return family;
            }
        }

        public static Font Ui(float size) { return new Font(Family, size * FontScale); }
        public static Font UiBold(float size)
        {
            return new Font(Family, size * FontScale, FontStyle.Bold);
        }
        public static Font UiPx(float px)
        {
            return new Font(Family, px * FontScale, GraphicsUnit.Pixel);
        }
        public static Font UiBoldPx(float px)
        {
            return new Font(Family, px * FontScale, FontStyle.Bold, GraphicsUnit.Pixel);
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

        // ---- Surfaces ---------------------------------------------------------------

        static TextureBrush scanlines;

        /// <summary>
        /// CRT scanlines: one darkened row in every three, from a cached 1x3
        /// texture — cheap enough to lay over every surface on every paint.
        /// <paramref name="phase"/> is the surface's screen Y, so the lines stay in
        /// step across control boundaries instead of restarting at each origin and
        /// betraying where the seams are.
        /// </summary>
        public static void Scanlines(Graphics g, Rectangle r, int phase)
        {
            if (scanlines == null)
            {
                var tile = new Bitmap(1, 3);
                tile.SetPixel(0, 2, Color.FromArgb(48, 0, 0, 0));
                scanlines = new TextureBrush(tile);
            }
            scanlines.ResetTransform();
            scanlines.TranslateTransform(0, -(phase % 3));
            g.FillRectangle(scanlines, r);
        }

        /// <summary>Clears a control to a flat colour and lays the scanlines over
        /// it. The handle exists by paint time, so PointToScreen is safe here.</summary>
        public static void Surface(Control c, Graphics g, Color back)
        {
            g.Clear(back);
            Scanlines(g, c.ClientRectangle, Phase(c));
        }

        public static int Phase(Control c)
        {
            try { return c.PointToScreen(Point.Empty).Y; }
            catch { return 0; }
        }

        public static void PaintCard(Graphics g, int w, int h) { PaintCard(g, w, h, 0); }

        /// <summary>
        /// A card: a flat slab, scanlines, a dashed rule around it and bright
        /// corner brackets. No shadow and no rounding — the terminal has no depth,
        /// it has phosphor and hard edges.
        /// </summary>
        public static void PaintCard(Graphics g, int w, int h, int phase)
        {
            var r = new Rectangle(0, 0, w - 1, h - 1);
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode was = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;   // 1px rules go blurry under AA
            using (var b = new SolidBrush(Card)) g.FillRectangle(b, r);
            Region clip = g.Clip;
            g.SetClip(r);
            Scanlines(g, r, phase);
            g.Clip = clip;
            using (var pen = new Pen(Line))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawRectangle(pen, r);
            }
            Brackets(g, r, Muted, 12);
            g.SmoothingMode = was;
        }

        /// <summary>The HUD framing every panel wears: an L at each corner, solid
        /// where the border itself is dashed.</summary>
        public static void Brackets(Graphics g, Rectangle r, Color c, int leg)
        {
            int x0 = r.X, y0 = r.Y, x1 = r.Right, y1 = r.Bottom;
            if (r.Width < leg * 3 || r.Height < leg * 2) return;
            using (var pen = new Pen(c))
            {
                g.DrawLines(pen, new[] { new Point(x0, y0 + leg), new Point(x0, y0), new Point(x0 + leg, y0) });
                g.DrawLines(pen, new[] { new Point(x1 - leg, y0), new Point(x1, y0), new Point(x1, y0 + leg) });
                g.DrawLines(pen, new[] { new Point(x1, y1 - leg), new Point(x1, y1), new Point(x1 - leg, y1) });
                g.DrawLines(pen, new[] { new Point(x0 + leg, y1), new Point(x0, y1), new Point(x0, y1 - leg) });
            }
        }

        // ---- Tracked text ---------------------------------------------------------

        const TextFormatFlags Plain = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        // One cell of the grid. Every glyph in a monospace face has the same
        // advance, so ten measured together and divided gives it without the
        // padding TextRenderer adds once per call — charging that padding per
        // character is what would otherwise triple the tracking.
        const string Ruler = "MMMMMMMMMM";

        static float Advance(Font f)
        {
            return TextRenderer.MeasureText(Ruler, f, Size.Empty, Plain).Width / 10f;
        }

        /// <summary>Letter-spaced text, the tracking the terminal headings wear.
        /// GDI+ has no such setting, so the run is stepped out by hand.</summary>
        public static void DrawTracked(Graphics g, string s, Font f, Point at, Color c, float track)
        {
            if (string.IsNullOrEmpty(s)) return;
            float step = Advance(f) + track;
            float x = at.X;
            for (int i = 0; i < s.Length; i++, x += step)
                TextRenderer.DrawText(g, s.Substring(i, 1), f,
                    new Point((int)Math.Round(x), at.Y), c, Plain);
        }

        public static int MeasureTracked(string s, Font f, float track)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return (int)Math.Ceiling(s.Length * (Advance(f) + track) - track);
        }

        /// <summary>Tracked text centred in a box. A label too wide to be stepped
        /// out falls back to the plain renderer, which can at least ellipsise it;
        /// DrawTracked would happily spill outside the control.</summary>
        public static void DrawTrackedCentered(Graphics g, string s, Font f,
            Rectangle r, Color c, float track)
        {
            int w = MeasureTracked(s, f, track);
            if (w > r.Width)
            {
                TextRenderer.DrawText(g, s, f, r, c, TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix);
                return;
            }
            DrawTracked(g, s, f,
                new Point(r.X + (r.Width - w) / 2, r.Y + (r.Height - LineHeight(g, f)) / 2),
                c, track);
        }

        public static void DrawTrackedLeft(Graphics g, string s, Font f,
            Rectangle r, Color c, float track)
        {
            if (MeasureTracked(s, f, track) > r.Width)
            {
                TextRenderer.DrawText(g, s, f, r, c, TextFormatFlags.Left
                    | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix);
                return;
            }
            DrawTracked(g, s, f, new Point(r.X, r.Y + (r.Height - LineHeight(g, f)) / 2), c, track);
        }

        static int LineHeight(Graphics g, Font f)
        {
            return TextRenderer.MeasureText(g, "X", f, Size.Empty, Plain).Height;
        }

        /// <summary>Plain left-aligned text, ellipsised to its box.</summary>
        public static void Draw(Graphics g, string s, Font f, Rectangle r, Color c)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextRenderer.DrawText(g, s, f, r, c, TextFormatFlags.Left
                | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        public static int Measure(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return TextRenderer.MeasureText(s, f, Size.Empty, Plain).Width;
        }

        // ---- Window chrome ----------------------------------------------------------

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // GDI COLORREF is 0x00BBGGRR — not the ARGB order Color uses.
        static int ToColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        /// <summary>
        /// Dark window chrome (Win10 1903+). On Windows 11 the caption is also
        /// painted in the app's own background colour so the title bar does not
        /// read as a foreign grey strip; those attributes fail harmlessly on
        /// Windows 10 and the dark caption survives.
        /// </summary>
        public static void DarkTitleBar(Form f)
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
                    int text = ToColorRef(Text);
                    DwmSetWindowAttribute(f.Handle, 36, ref text, 4);     // DWMWA_TEXT_COLOR
                    int border = ToColorRef(Line);
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
