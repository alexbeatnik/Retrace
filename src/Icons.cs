// Every glyph in the app, drawn as vector paths.
//
// Not typed as Unicode transport characters: those resolve to whichever symbol
// font Windows picks, arrive at a size and baseline nothing else agrees with,
// and the shuffle and repeat ones come back as colour emoji. Paths sit exactly
// where the layout puts them and take the colour they are given — which is what
// lets the whole app follow a palette change.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Retrace
{
    delegate void IconDraw(Graphics g, RectangleF r, Color c);

    static class Ico
    {
        /// <summary>The largest square that fits, centred and scaled down a little
        /// so a glyph never touches the edge of what it sits in.</summary>
        static RectangleF Box(RectangleF r, float fill)
        {
            float s = Math.Min(r.Width, r.Height) * fill;
            return new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s);
        }

        static void Fill(Graphics g, Color c, PointF[] pts)
        {
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
        }

        static PointF[] Triangle(RectangleF b, bool pointRight)
        {
            return pointRight
                ? new[] { new PointF(b.Left, b.Top),
                          new PointF(b.Right, b.Top + b.Height / 2f),
                          new PointF(b.Left, b.Bottom) }
                : new[] { new PointF(b.Right, b.Top),
                          new PointF(b.Left, b.Top + b.Height / 2f),
                          new PointF(b.Right, b.Bottom) };
        }

        // ---- Transport --------------------------------------------------------

        public static void Play(Graphics g, RectangleF r, Color c)
        {
            var b = Box(r, 0.52f);
            // Nudged right by a tenth: a triangle centred on its bounding box
            // reads as sitting left of centre, because its mass is on that side.
            b.Offset(b.Width * 0.10f, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Fill(g, c, Triangle(b, true));
        }

        public static void Pause(Graphics g, RectangleF r, Color c)
        {
            var b = Box(r, 0.46f);
            float w = b.Width * 0.34f;
            g.SmoothingMode = SmoothingMode.None;
            using (var br = new SolidBrush(c))
            {
                g.FillRectangle(br, b.Left, b.Top, w, b.Height);
                g.FillRectangle(br, b.Right - w, b.Top, w, b.Height);
            }
        }

        public static void Stop(Graphics g, RectangleF r, Color c)
        {
            g.SmoothingMode = SmoothingMode.None;
            using (var br = new SolidBrush(c)) g.FillRectangle(br, Box(r, 0.42f));
        }

        public static void Previous(Graphics g, RectangleF r, Color c) { Skip(g, r, c, false); }
        public static void Next(Graphics g, RectangleF r, Color c) { Skip(g, r, c, true); }

        /// <summary>Two stacked triangles against a bar, the way a deck marks its
        /// cue and review keys.</summary>
        static void Skip(Graphics g, RectangleF r, Color c, bool forward)
        {
            var b = Box(r, 0.52f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float bar = Math.Max(1.5f, b.Width * 0.15f);
            float wedge = (b.Width - bar) / 2f;

            var first = new RectangleF(forward ? b.Left : b.Left + bar, b.Top, wedge, b.Height);
            var second = new RectangleF(forward ? b.Left + wedge : b.Left + bar + wedge,
                b.Top, wedge, b.Height);
            Fill(g, c, Triangle(first, forward));
            Fill(g, c, Triangle(second, forward));

            g.SmoothingMode = SmoothingMode.None;
            using (var br = new SolidBrush(c))
                g.FillRectangle(br, forward ? b.Right - bar : b.Left, b.Top, bar, b.Height);
        }

        /// <summary>Eject: the triangle and the line under it, unchanged since the
        /// first cassette deck to carry one.</summary>
        public static void Eject(Graphics g, RectangleF r, Color c)
        {
            var b = Box(r, 0.50f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float gap = b.Height * 0.22f;
            float barH = Math.Max(1.5f, b.Height * 0.16f);
            float triH = b.Height - gap - barH;
            Fill(g, c, new[] {
                new PointF(b.Left + b.Width / 2f, b.Top),
                new PointF(b.Right, b.Top + triH),
                new PointF(b.Left, b.Top + triH) });
            g.SmoothingMode = SmoothingMode.None;
            using (var br = new SolidBrush(c))
                g.FillRectangle(br, b.Left, b.Bottom - barH, b.Width, barH);
        }

        /// <summary>Shuffle: two paths that cross, with an arrowhead on each.</summary>
        public static void Shuffle(Graphics g, RectangleF r, Color c)
        {
            var b = Box(r, 0.62f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float head = b.Width * 0.24f;
            using (var pen = new Pen(c, Math.Max(1.2f, b.Width * 0.11f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, b.Left, b.Top + b.Height * 0.22f,
                                b.Right - head * 0.7f, b.Bottom - b.Height * 0.22f);
                g.DrawLine(pen, b.Left, b.Bottom - b.Height * 0.22f,
                                b.Right - head * 0.7f, b.Top + b.Height * 0.22f);
            }
            Arrow(g, c, new PointF(b.Right, b.Bottom - b.Height * 0.22f), head, 1);
            Arrow(g, c, new PointF(b.Right, b.Top + b.Height * 0.22f), head, -1);
        }

        static void Arrow(Graphics g, Color c, PointF tip, float size, float dir)
        {
            Fill(g, c, new[] {
                tip,
                new PointF(tip.X - size, tip.Y - size * 0.55f * dir),
                new PointF(tip.X - size * 0.65f, tip.Y + size * 0.5f * dir) });
        }

        public static void RepeatAll(Graphics g, RectangleF r, Color c) { Repeat(g, r, c, false); }
        public static void RepeatOne(Graphics g, RectangleF r, Color c) { Repeat(g, r, c, true); }

        /// <summary>A loop with an arrowhead — and a "1" inside it for the
        /// repeat-one state, exactly as a CD player marks it.</summary>
        static void Repeat(Graphics g, RectangleF r, Color c, bool one)
        {
            var b = Box(r, 0.66f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Math.Max(1.2f, b.Width * 0.11f);
            // The arc is left open at the top right so the arrowhead has somewhere
            // to sit; a closed loop with a head on it reads as a smudge.
            using (var pen = new Pen(c, w))
                g.DrawArc(pen, b.Left + w / 2, b.Top + w / 2,
                    b.Width - w, b.Height - w, -55, 305);

            float head = b.Width * 0.30f;
            var tip = new PointF(b.Right - w, b.Top + b.Height * 0.16f);
            Fill(g, c, new[] {
                new PointF(tip.X + head * 0.20f, tip.Y + head * 0.50f),
                new PointF(tip.X - head * 0.62f, tip.Y + head * 0.18f),
                new PointF(tip.X - head * 0.16f, tip.Y - head * 0.52f) });

            if (one)
                using (var f = new Font(Theme.Family, b.Height * 0.46f,
                           FontStyle.Bold, GraphicsUnit.Pixel))
                using (var br = new SolidBrush(c))
                using (var fmt = new StringFormat())
                {
                    fmt.Alignment = StringAlignment.Center;
                    fmt.LineAlignment = StringAlignment.Center;
                    g.DrawString("1", f, br, b, fmt);
                }
        }

        // ---- Navigation --------------------------------------------------------

        /// <summary>A speaker with two waves: the player page.</summary>
        public static void Speaker(Graphics g, RectangleF r, Color c)
        {
            var b = Box(r, 0.66f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float coneW = b.Width * 0.34f;
            Fill(g, c, new[] {
                new PointF(b.Left, b.Top + b.Height * 0.34f),
                new PointF(b.Left + coneW * 0.7f, b.Top + b.Height * 0.34f),
                new PointF(b.Left + coneW * 1.5f, b.Top),
                new PointF(b.Left + coneW * 1.5f, b.Bottom),
                new PointF(b.Left + coneW * 0.7f, b.Bottom - b.Height * 0.34f),
                new PointF(b.Left, b.Bottom - b.Height * 0.34f) });
            using (var pen = new Pen(c, Math.Max(1f, b.Width * 0.09f)))
                for (int i = 0; i < 2; i++)
                {
                    float rad = b.Width * (0.20f + i * 0.16f);
                    float cx = b.Left + coneW * 1.6f, cy = b.Top + b.Height / 2f;
                    g.DrawArc(pen, cx - rad, cy - rad, rad * 2, rad * 2, -55, 110);
                }
        }

        /// <summary>Three stacked rules: the playlist page.</summary>
        public static void List(Graphics g, RectangleF r, Color c)
        {
            var b = Box(r, 0.62f);
            g.SmoothingMode = SmoothingMode.None;
            float gap = b.Height / 3f;
            float h = Math.Max(1.2f, gap * 0.40f);
            using (var br = new SolidBrush(c))
                for (int i = 0; i < 3; i++)
                {
                    g.FillRectangle(br, b.Left, b.Top + gap * i + (gap - h) / 2f, h * 1.6f, h);
                    g.FillRectangle(br, b.Left + h * 3f, b.Top + gap * i + (gap - h) / 2f,
                        b.Width - h * 3f, h);
                }
        }

        /// <summary>The slider stack that marks an equaliser.</summary>
        public static void Equaliser(Graphics g, RectangleF r, Color c)
        {
            var b = Box(r, 0.64f);
            g.SmoothingMode = SmoothingMode.None;
            float step = b.Width / 3f;
            float w = Math.Max(1.4f, step * 0.30f);
            var heights = new[] { 0.45f, 0.85f, 0.62f };
            using (var br = new SolidBrush(c))
                for (int i = 0; i < 3; i++)
                {
                    float h = b.Height * heights[i];
                    float x = b.Left + step * i + (step - w) / 2f;
                    g.FillRectangle(br, x, b.Bottom - h, w, h);
                    // The cap on each fader, so it reads as a control and not as a
                    // bar chart.
                    g.FillRectangle(br, x - w * 0.6f, b.Bottom - h - w, w * 2.2f, w);
                }
        }

        /// <summary>A cogwheel: the settings page.</summary>
        public static void Gear(Graphics g, RectangleF r, Color c)
        {
            var b = Box(r, 0.70f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = b.Left + b.Width / 2f, cy = b.Top + b.Height / 2f;
            float outer = b.Width * 0.5f, inner = outer * 0.66f;
            using (var path = new GraphicsPath())
            {
                const int teeth = 8;
                var pts = new PointF[teeth * 4];
                for (int i = 0; i < teeth * 4; i++)
                {
                    // Alternating pairs of radii walk the outline out over a tooth
                    // and back into the gap between them.
                    double a = i * Math.PI * 2 / (teeth * 4) - Math.PI / 2;
                    float rad = (i % 4 == 1 || i % 4 == 2) ? outer : inner;
                    pts[i] = new PointF(cx + (float)(Math.Cos(a) * rad),
                                        cy + (float)(Math.Sin(a) * rad));
                }
                path.AddPolygon(pts);
                using (var br = new SolidBrush(c)) g.FillPath(br, path);
            }
            // The bore, cut out by painting the card ground back over the middle.
            using (var br = new SolidBrush(Theme.Card))
                g.FillEllipse(br, cx - outer * 0.34f, cy - outer * 0.34f,
                    outer * 0.68f, outer * 0.68f);
        }

        // ---- Small marks ---------------------------------------------------------

        public static void Folder(Graphics g, RectangleF r, Color c)
        {
            var b = Box(r, 0.62f);
            g.SmoothingMode = SmoothingMode.None;
            float tab = b.Height * 0.22f;
            using (var br = new SolidBrush(c))
            {
                g.FillRectangle(br, b.Left, b.Top + tab * 0.6f, b.Width * 0.42f, tab);
                g.FillRectangle(br, b.Left, b.Top + tab * 1.4f, b.Width, b.Height - tab * 1.4f);
            }
        }

        public static void File(Graphics g, RectangleF r, Color c)
        {
            var b = Box(r, 0.58f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float fold = b.Width * 0.34f;
            Fill(g, c, new[] {
                new PointF(b.Left, b.Top), new PointF(b.Right - fold, b.Top),
                new PointF(b.Right, b.Top + fold), new PointF(b.Right, b.Bottom),
                new PointF(b.Left, b.Bottom) });
        }

        public static void Cross(Graphics g, RectangleF r, Color c)
        {
            var b = Box(r, 0.44f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(c, Math.Max(1.4f, b.Width * 0.16f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, b.Left, b.Top, b.Right, b.Bottom);
                g.DrawLine(pen, b.Right, b.Top, b.Left, b.Bottom);
            }
        }

        public static void Minus(Graphics g, RectangleF r, Color c)
        {
            var b = Box(r, 0.46f);
            g.SmoothingMode = SmoothingMode.None;
            using (var br = new SolidBrush(c))
                g.FillRectangle(br, b.Left, b.Top + b.Height / 2f - 1, b.Width, 2);
        }

        public static void Save(Graphics g, RectangleF r, Color c) { Arrowhead(g, r, c, true); }
        public static void Load(Graphics g, RectangleF r, Color c) { Arrowhead(g, r, c, false); }

        /// <summary>An arrow into or out of a tray: save and load.</summary>
        static void Arrowhead(Graphics g, RectangleF r, Color c, bool down)
        {
            var b = Box(r, 0.60f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float stem = b.Width * 0.18f;
            float headH = b.Height * 0.34f;
            float trayY = b.Bottom - b.Height * 0.18f;
            float cx = b.Left + b.Width / 2f;

            if (down)
            {
                using (var br = new SolidBrush(c))
                    g.FillRectangle(br, cx - stem / 2, b.Top, stem, b.Height * 0.42f);
                Fill(g, c, new[] {
                    new PointF(cx - b.Width * 0.26f, b.Top + b.Height * 0.40f),
                    new PointF(cx + b.Width * 0.26f, b.Top + b.Height * 0.40f),
                    new PointF(cx, b.Top + b.Height * 0.40f + headH) });
            }
            else
            {
                Fill(g, c, new[] {
                    new PointF(cx - b.Width * 0.26f, b.Top + headH),
                    new PointF(cx + b.Width * 0.26f, b.Top + headH),
                    new PointF(cx, b.Top) });
                using (var br = new SolidBrush(c))
                    g.FillRectangle(br, cx - stem / 2, b.Top + headH, stem, b.Height * 0.30f);
            }
            using (var pen = new Pen(c, Math.Max(1.2f, b.Width * 0.10f)))
                g.DrawLines(pen, new[] {
                    new PointF(b.Left, trayY - b.Height * 0.10f),
                    new PointF(b.Left, trayY),
                    new PointF(b.Right, trayY),
                    new PointF(b.Right, trayY - b.Height * 0.10f) });
        }

        /// <summary>The mark on the window's own header: a phosphor waveform.</summary>
        public static void Wave(Graphics g, RectangleF r, Color c)
        {
            g.SmoothingMode = SmoothingMode.None;
            var heights = new[] { 0.30f, 0.62f, 0.88f, 0.54f, 0.96f, 0.42f, 0.70f, 0.34f };
            float step = r.Width / heights.Length;
            float w = Math.Max(1.5f, step * 0.52f);
            using (var br = new SolidBrush(c))
                for (int i = 0; i < heights.Length; i++)
                {
                    float h = r.Height * heights[i];
                    g.FillRectangle(br, r.X + step * i + (step - w) / 2f,
                        r.Y + (r.Height - h) / 2f, w, h);
                }
        }
    }
}
