// The app mark and the ICO writer that turns it into the executable's icon at
// build time (build.ps1 pass 1 runs Retrace.exe --write-icon app.ico, pass 2
// embeds the result). Generating the icon rather than committing one keeps the
// repository free of binary assets and stops the taskbar icon drifting away
// from the player it stands for.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Retrace
{
    static class Brand
    {
        public const string Product = "Retrace";
        public const string Version = "1.0.0";

        // 256 is what Explorer's extra-large view wants; 16/20/24 are the tray,
        // the taskbar and the title bar.
        internal static readonly int[] IconSizes = { 16, 20, 24, 32, 48, 64, 128, 256 };

        /// <summary>
        /// The mark: an amber level meter on a near-black tile.
        ///
        /// Deliberately plainer than a card in the UI — no dashed rule, no corner
        /// brackets — because this is read at 16 and 24 pixels on a taskbar,
        /// where a frame closes up into a smudge and only the bars survive.
        /// Drawn into an arbitrary rectangle so the same code serves every size
        /// in the icon resource.
        ///
        /// It always uses the default scheme rather than the current one — the
        /// icon is written by the build, long before any settings exist, and an
        /// icon that changed with a preference would leave Explorer showing
        /// whichever version it cached first.
        /// </summary>
        public static void PaintMark(Graphics g, RectangleF r)
        {
            float s = Math.Min(r.Width, r.Height);
            var box = new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s);
            Palette scheme = Palette.All[0];

            // The tile is the one rounded thing in the app. Everything else on
            // the taskbar is set in a rounded square, and a hard-cornered black
            // block beside them reads as a missing icon rather than as a mark.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath tile = RoundedTile(box, s * 0.20f))
            using (var b = new SolidBrush(Color.FromArgb(14, 14, 14)))
                g.FillPath(b, tile);

            // Fixed heights rather than random: an icon has to look the same
            // every time it is drawn, and Explorer caches whatever it gets.
            var heights = new[] { 0.34f, 0.68f, 1.00f, 0.50f, 0.86f, 0.40f };

            // Bar and gap are whole pixels taken from the size, not fractions of
            // a layout rectangle: at 16 and 24 a fractional step rounds two
            // neighbours into each other and the meter turns into a blob. These
            // ratios keep the block at roughly 70% of the tile at every size.
            int bw = Math.Max(1, Px(s * 0.085f));
            int gap = Math.Max(1, Px(s * 0.045f));
            int span = heights.Length * bw + (heights.Length - 1) * gap;
            int x = Px(box.X + (s - span) / 2f);
            float cy = box.Y + s / 2f;

            // Square ends and no antialiasing: at these widths a feathered edge
            // spends half the bar turning amber into mud.
            g.SmoothingMode = SmoothingMode.None;
            using (Brush b = BarBrush(scheme, box, s))
                for (int i = 0; i < heights.Length; i++, x += bw + gap)
                {
                    int h = Math.Max(1, Px(s * 0.62f * heights[i]));
                    g.FillRectangle(b, x, Px(cy - h / 2f), bw, h);
                }
        }

        static int Px(float v) { return (int)Math.Round(v, MidpointRounding.AwayFromZero); }

        /// <summary>Amber, a shade hotter at the top than at the bottom so the
        /// bars read as lit rather than painted. The hot end is only halfway to
        /// the palette's Bright: taken the whole way it washes out to pale yellow
        /// and stops being amber. Flat below 32, where a gradient spanning ten
        /// pixels only muddies the hue.</summary>
        static Brush BarBrush(Palette scheme, RectangleF box, float s)
        {
            if (s < 32) return new SolidBrush(scheme.Text);
            Color hot = Color.FromArgb(
                (scheme.Text.R + scheme.Bright.R) / 2,
                (scheme.Text.G + scheme.Bright.G) / 2,
                (scheme.Text.B + scheme.Bright.B) / 2);
            // The gradient rectangle is drawn a little taller than the tallest
            // bar; a brush whose rectangle stops short of the fill tiles itself.
            return new LinearGradientBrush(
                new RectangleF(box.X, box.Y + s * 0.17f, s, s * 0.66f),
                hot, scheme.Text, LinearGradientMode.Vertical);
        }

        /// <summary>A rounded square as a path, so it fills in one call.</summary>
        static GraphicsPath RoundedTile(RectangleF box, float radius)
        {
            float d = Math.Max(1f, radius) * 2f;
            var p = new GraphicsPath();
            p.AddArc(box.X, box.Y, d, d, 180, 90);
            p.AddArc(box.Right - d, box.Y, d, d, 270, 90);
            p.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
            p.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Bitmap MarkBitmap(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                PaintMark(g, new RectangleF(0, 0, size, size));
            }
            return bmp;
        }

        /// <summary>
        /// An ICO container with one PNG-compressed entry per size. PNG entries
        /// are understood by every Windows the 4.8 runtime installs on, and they
        /// keep the 256x256 from bloating the file to a megabyte.
        /// </summary>
        internal static byte[] IcoBytes(int[] sizes)
        {
            var images = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = MarkBitmap(s))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
                }

            using (var outMs = new MemoryStream())
            using (var w = new BinaryWriter(outMs))
            {
                w.Write((short)0);              // reserved
                w.Write((short)1);              // type: 1 = icon
                w.Write((short)images.Count);
                int offset = 6 + 16 * images.Count;
                for (int i = 0; i < images.Count; i++)
                {
                    // 256 is stored as 0 in the single-byte width and height fields
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0);           // palette size (0 = truecolour)
                    w.Write((byte)0);           // reserved
                    w.Write((short)1);          // colour planes
                    w.Write((short)32);         // bits per pixel
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] img in images) w.Write(img);
                w.Flush();
                return outMs.ToArray();
            }
        }

        static Icon appIcon;

        /// <summary>The multi-resolution icon, built in memory — no file on disk
        /// and no GetHicon handle to leak.</summary>
        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try
                    {
                        using (var ms = new MemoryStream(IcoBytes(IconSizes)))
                            appIcon = new Icon(ms);
                    }
                    catch (ArgumentException) { appIcon = SystemIcons.Application; }
                    catch (IOException) { appIcon = SystemIcons.Application; }
                }
                return appIcon;
            }
        }

        /// <summary>--write-icon &lt;path&gt;: the build's first pass calls this so
        /// the second pass has something to hand /win32icon.</summary>
        public static int WriteIconFile(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, IcoBytes(IconSizes));
                return 0;
            }
            catch (IOException) { return 1; }
            catch (UnauthorizedAccessException) { return 1; }
            catch (ArgumentException) { return 1; }
        }
    }
}
