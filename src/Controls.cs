// The controls every page is assembled from: cards, nav tabs, buttons, the stat
// strip, the two kinds of slider, the analyser and the playlist.
//
// All custom-drawn. The stock controls cannot be themed far enough — a
// ListView's scrollbar and header stay system-light however dark everything
// around them is — and a palette that can change at run time needs every pixel
// to come from Theme rather than from a colour baked in at construction.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Retrace
{
    /// <summary>Base for everything here: double-buffered, repaints itself when
    /// the scheme changes, and unhooks cleanly.</summary>
    class Themed : Control
    {
        public Themed()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Theme.Changed += OnThemeChanged;
        }

        void OnThemeChanged(object sender, EventArgs e)
        {
            // The event fires from whatever thread changed the palette; in this app
            // that is always the UI thread, but a disposed control still has to be
            // safe because the handler outlives the control until Dispose runs.
            if (!IsDisposed) Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Theme.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }
    }

    /// <summary>A page background: the flat ground with the scanlines on it. A
    /// plain Panel paints a clean slab and breaks the lines mid-window.</summary>
    class CrtPanel : Themed
    {
        public Color Ground = Theme.Bg;

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Surface(this, e.Graphics, Ground);
            // The base implementation raises the Paint event, and it has to run
            // after the ground is laid down: the header's wordmark and the status
            // line are attached that way, and without this call neither appears.
            base.OnPaint(e);
        }
    }

    /// <summary>A card: the dashed rule, the corner brackets and an uppercase
    /// header along the top.</summary>
    class Card : Themed
    {
        public string Header = "";
        /// <summary>Drawn right-aligned in the header row — a count, a total, a
        /// state word.</summary>
        public string Note = "";

        public const int HeaderH = 30;

        public Card(string header)
        {
            Header = header;
            Padding = new Padding(14, HeaderH + 6, 14, 12);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.PaintCard(g, Width, Height, Theme.Phase(this));
            int noteX = 14;
            if (Header.Length > 0)
                using (var f = Theme.UiBold(9f))
                {
                    string caption = Header.ToUpperInvariant();
                    Theme.DrawTracked(g, caption, f, new Point(14, 11), Theme.Bright, Theme.Track);
                    noteX += Theme.MeasureTracked(caption, f, Theme.Track) + 18;
                }
            if (Note.Length > 0)
                using (var f = Theme.Ui(8.5f))
                    Theme.DrawTracked(g, Note, f, new Point(noteX, 12), Theme.Muted, Theme.Track);
            // The base implementation raises the Paint event, and it has to run
            // after the card is laid down: a page draws its readouts into the card
            // through that event, and without this call they never appear.
            base.OnPaint(e);
        }
    }

    /// <summary>
    /// A row of caption-over-value readouts on a card, the shape both sister apps
    /// use for their numbers.
    /// </summary>
    class StatStrip : Themed
    {
        public string[] Captions = new string[0];
        public string[] Values = new string[0];

        public StatStrip() { Height = 62; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.PaintCard(g, Width, Height, Theme.Phase(this));
            using (var capF = Theme.UiBold(8f))
            using (var valF = Theme.UiBold(13f))
            {
                int x = 18;
                for (int i = 0; i < Captions.Length; i++)
                {
                    string cap = Captions[i].ToUpperInvariant();
                    string val = i < Values.Length ? Values[i] : "";
                    int cell = Math.Max(Theme.MeasureTracked(cap, capF, Theme.Track),
                                        Theme.Measure(val, valF));
                    Theme.DrawTracked(g, cap, capF, new Point(x, 13), Theme.Muted, Theme.Track);
                    Theme.Draw(g, val, valF, new Rectangle(x, 28, cell + 6, 24), Theme.Text);
                    x += cell + 26;
                    if (x > Width - 20) break;
                }
            }
        }
    }

    /// <summary>A tab in the header. The active one is a filled block, which is
    /// the only place the phosphor is used as a ground.</summary>
    class NavTab : Themed
    {
        public IconDraw Icon;
        public bool Active;
        bool over;

        public NavTab(string text, IconDraw icon)
        {
            Text = text;
            Icon = icon;
            Height = 30;
            Cursor = Cursors.Hand;
            Font = Theme.UiBold(9f);
            MouseEnter += delegate { over = true; Invalidate(); };
            MouseLeave += delegate { over = false; Invalidate(); };
        }

        public void SetActive(bool a) { Active = a; Invalidate(); }

        /// <summary>
        /// Sizes the tab to its own caption. Auto-sizing through a layout panel
        /// measures before the text has been set and clips the first tab, so the
        /// header positions these by hand and calls this after every language
        /// change.
        /// </summary>
        public void FitWidth()
        {
            using (var f = Theme.UiBold(9f))
                Width = 34 + Theme.MeasureTracked(Text.ToUpperInvariant(), f, Theme.Track) + 14;
            // OnTextChanged does not repaint a UserPaint control: only ResizeRedraw
            // invalidates, and a translated caption of the same length measures to
            // the same width, so the resize never happens.
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Surface(this, g, Theme.Bg);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            if (Active)
                using (var b = new SolidBrush(Theme.Text)) g.FillRectangle(b, r);
            else if (over)
                using (var b = new SolidBrush(Theme.Subtle)) g.FillRectangle(b, r);

            Color ink = Active ? Theme.OnAccent : (over ? Theme.Bright : Theme.Muted);
            if (Icon != null) Icon(g, new RectangleF(9, 6, 18, 18), ink);
            using (var f = Theme.UiBold(9f))
                Theme.DrawTracked(g, Text.ToUpperInvariant(), f, new Point(32, 9), ink, Theme.Track);
        }
    }

    /// <summary>
    /// A push button. Square, dashed-free, with the phosphor as its text — the
    /// primary variant fills instead, for the one action a page is really about.
    /// </summary>
    class Btn : Themed, IButtonControl
    {
        public IconDraw Icon;
        /// <summary>Latching: draws lit when on, which is how the scheme, the
        /// language and the equaliser switch say which one is chosen.</summary>
        public bool Latch, On;

        bool over, down;
        DialogResult dialogResult = DialogResult.None;

        public Btn(string text)
        {
            Text = text;
            Height = 30;
            Width = 96;
            Cursor = Cursors.Hand;
            Font = Theme.UiBold(9f);
            MouseEnter += delegate { over = true; Invalidate(); };
            MouseLeave += delegate { over = false; down = false; Invalidate(); };
            MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { down = true; Invalidate(); }
            };
            MouseUp += delegate { down = false; Invalidate(); };
        }

        public DialogResult DialogResult
        {
            get { return dialogResult; }
            set { dialogResult = value; }
        }
        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

        /// <summary>Sizes the button to its caption, with room for the glyph.</summary>
        public void FitWidth(int minimum)
        {
            using (var f = Theme.UiBold(9f))
            {
                int text = Theme.MeasureTracked(Text.ToUpperInvariant(), f, Theme.Track);
                Width = Math.Max(minimum, (Icon != null ? 30 : 0) + text + 26);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Surface(this, g, Parent != null ? Parent.BackColor : Theme.Bg);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            bool lit = Latch && On;
            Color fill = !Enabled ? Color.FromArgb(20, 20, 20)
                       : lit ? (over ? Theme.Bright : Theme.Text)
                       : (down ? Theme.Card : (over ? Theme.BtnHot : Theme.Subtle));
            using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);

            Color edge = !Enabled ? Color.FromArgb(48, 48, 48) : lit ? Theme.Bright : Theme.Line;
            using (var pen = new Pen(edge)) g.DrawRectangle(pen, r);

            Color ink = !Enabled ? Theme.Disabled
                      : lit ? Theme.OnAccent
                      : (over ? Theme.Bright : Theme.Text);

            using (var f = Theme.UiBold(9f))
            {
                string caption = Text.ToUpperInvariant();
                int textW = Theme.MeasureTracked(caption, f, Theme.Track);
                int glyph = Icon != null ? 20 : 0;
                int x = (Width - (glyph + (glyph > 0 && textW > 0 ? 8 : 0) + textW)) / 2;
                if (down && Enabled) x += 1;
                int y = (Height - 14) / 2 + (down && Enabled ? 1 : 0);
                if (Icon != null) Icon(g, new RectangleF(x, y - 2, 18, 18), ink);
                if (textW > 0)
                    Theme.DrawTracked(g, caption, f, new Point(x + glyph + (glyph > 0 ? 8 : 0), y),
                        ink, Theme.Track);
            }
        }
    }

    /// <summary>
    /// A square transport key. Separate from Btn because the transport is a row
    /// of glyphs with no captions, and it wants to stay square whatever the
    /// language does to everything else.
    /// </summary>
    class KeyBtn : Themed, IButtonControl
    {
        public IconDraw Icon;
        public bool Primary;
        /// <summary>Latching: draws lit when on, which is how shuffle and repeat
        /// say what they are doing without needing a caption.</summary>
        public bool Latch, On;
        bool over, down;
        DialogResult dialogResult = DialogResult.None;

        public KeyBtn(IconDraw icon)
        {
            Icon = icon;
            Size = new Size(46, 46);
            Cursor = Cursors.Hand;
        }

        public DialogResult DialogResult
        {
            get { return dialogResult; }
            set { dialogResult = value; }
        }
        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

        protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { over = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { down = true; Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Surface(this, g, Theme.Card);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            bool lit = Primary || (Latch && On);
            Color fill = !Enabled ? Color.FromArgb(16, 16, 16)
                       : lit ? (over ? Theme.Bright : Theme.Text)
                       : (down ? Color.FromArgb(6, 6, 6) : (over ? Theme.Subtle : Color.FromArgb(20, 20, 20)));
            using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
            using (var pen = new Pen(!Enabled ? Color.FromArgb(40, 40, 40)
                       : lit ? Theme.Bright : Theme.Line))
                g.DrawRectangle(pen, r);

            Color ink = !Enabled ? Theme.Disabled
                      : lit ? Theme.OnAccent : (over ? Theme.Bright : Theme.Text);
            var box = new RectangleF(0, down && Enabled ? 1 : 0, Width, Height);
            if (Icon != null) Icon(g, box, ink);
        }
    }

    /// <summary>
    /// A horizontal slider drawn as a phosphor bar: a dashed empty track, a solid
    /// filled run, and a bright cursor at the setting. Used for position, volume
    /// and balance, which differ only in what they are measuring.
    /// </summary>
    class Bar : Themed
    {
        public double Min = 0, Max = 1;
        /// <summary>Fills outward from the middle rather than from the left —
        /// what balance needs and volume does not.</summary>
        public bool FromCentre;
        /// <summary>Snaps to the middle within this fraction of the travel, so a
        /// balance control can be re-centred by hand.</summary>
        public double Detent;

        double value;
        bool over, dragging;

        /// <summary>Raised continuously while dragging and once on release.</summary>
        public event EventHandler ValueChanged;
        /// <summary>True from press to release: the owner stops writing the value
        /// in from elsewhere while the hand is on it.</summary>
        public bool Dragging { get { return dragging; } }

        public Bar()
        {
            Height = 22;
            Cursor = Cursors.Hand;
        }

        public double Value
        {
            get { return value; }
            set
            {
                double v = value < Min ? Min : (value > Max ? Max : value);
                if (Math.Abs(v - this.value) < 1e-6) return;
                this.value = v;
                Invalidate();
            }
        }

        /// <summary>Sets the position without announcing it — for restoring saved
        /// state, where firing the event would write it straight back.</summary>
        public void SetSilent(double v)
        {
            this.value = v < Min ? Min : (v > Max ? Max : v);
            Invalidate();
        }

        double Fraction
        {
            get { return Max > Min ? (value - Min) / (Max - Min) : 0; }
        }

        void Announce()
        {
            EventHandler h = ValueChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        void SetFromMouse(int x)
        {
            double f = (x - 2) / (double)Math.Max(1, Width - 4);
            if (f < 0) f = 0;
            if (f > 1) f = 1;
            if (Detent > 0 && Math.Abs(f - 0.5) < Detent) f = 0.5;
            value = Min + f * (Max - Min);
            Invalidate();
            Announce();
        }

        protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { over = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                Capture = true;
                SetFromMouse(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) SetFromMouse(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragging)
            {
                dragging = false;
                Capture = false;
                // One last event after the flag drops, so the owner applies the
                // final value and starts following its own source again.
                Announce();
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (Detent > 0) { Value = (Min + Max) / 2; Announce(); }
            base.OnMouseDoubleClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Surface(this, g, Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.None;

            int mid = Height / 2;
            var track = new Rectangle(2, mid - 3, Width - 4, 6);
            using (var b = new SolidBrush(Theme.Sunken)) g.FillRectangle(b, track);
            using (var pen = new Pen(Theme.Line))
            {
                pen.DashStyle = DashStyle.Dot;
                g.DrawLine(pen, track.X, mid, track.Right - 1, mid);
            }

            float f = (float)Fraction;
            int at = track.X + (int)(track.Width * f);
            int from = FromCentre ? Math.Min(track.X + track.Width / 2, at) : track.X;
            int to = FromCentre ? Math.Max(track.X + track.Width / 2, at) : at;
            if (to > from)
                using (var b = new SolidBrush(over || dragging ? Theme.Bright : Theme.Text))
                    g.FillRectangle(b, from, mid - 3, to - from, 6);

            // The cursor: a full-height bar, so the setting is findable even when
            // the fill behind it is only a pixel or two wide.
            var cursor = new Rectangle(Math.Min(at, track.Right - 3), 0, 3, Height);
            using (var b = new SolidBrush(over || dragging ? Theme.Bright : Theme.Text))
                g.FillRectangle(b, cursor);
        }
    }

    /// <summary>
    /// A vertical fader, the kind a graphic equaliser has eleven of. Same phosphor
    /// language as Bar, turned on its side and filling from the centre detent.
    /// </summary>
    class Fader : Themed
    {
        public double Min = -12, Max = 12;
        public string Label = "";

        double value;
        bool over, dragging;

        public event EventHandler ValueChanged;

        public Fader()
        {
            Size = new Size(34, 150);
            Cursor = Cursors.SizeNS;
        }

        public double Value
        {
            get { return value; }
            set
            {
                double v = value < Min ? Min : (value > Max ? Max : value);
                if (Math.Abs(v - this.value) < 1e-6) return;
                this.value = v;
                Invalidate();
                EventHandler h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public void SetSilent(double v)
        {
            this.value = v < Min ? Min : (v > Max ? Max : v);
            Invalidate();
        }

        const int LabelH = 18, ValueH = 16;
        int TravelTop { get { return ValueH + 4; } }
        int TravelH { get { return Math.Max(1, Height - LabelH - TravelTop - 4); } }

        void SetFromMouse(int y)
        {
            double f = (y - TravelTop) / (double)TravelH;
            if (f < 0) f = 0;
            if (f > 1) f = 1;
            double v = Max - f * (Max - Min);
            if (Math.Abs(v) < (Max - Min) * 0.04) v = 0;   // a detent at flat
            Value = v;
        }

        protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { over = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                Capture = true;
                SetFromMouse(e.Y);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) SetFromMouse(e.Y);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false; Capture = false; base.OnMouseUp(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            Value = 0; base.OnMouseDoubleClick(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Value = value + (e.Delta > 0 ? 1 : -1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Surface(this, g, Theme.Card);
            g.SmoothingMode = SmoothingMode.None;

            int cx = Width / 2;
            var track = new Rectangle(cx - 3, TravelTop, 6, TravelH);
            using (var b = new SolidBrush(Theme.Sunken)) g.FillRectangle(b, track);
            using (var pen = new Pen(Theme.Line))
            {
                pen.DashStyle = DashStyle.Dot;
                g.DrawLine(pen, cx, track.Y, cx, track.Bottom - 1);
            }

            int zero = TravelTop + (int)(TravelH * (Max / (Max - Min)));
            using (var pen = new Pen(Theme.Line))
                g.DrawLine(pen, cx - 8, zero, cx + 7, zero);

            double f = (Max - value) / (Max - Min);
            int at = TravelTop + (int)(TravelH * f);
            Color lit = over || dragging ? Theme.Bright : Theme.Text;
            if (at != zero)
                using (var b = new SolidBrush(lit))
                    g.FillRectangle(b, cx - 3, Math.Min(at, zero), 6, Math.Abs(zero - at));

            using (var b = new SolidBrush(lit)) g.FillRectangle(b, cx - 9, at - 2, 18, 4);

            using (var vf = Theme.UiBold(8.5f))
            {
                string shown = (value > 0 ? "+" : "") + value.ToString("0.#",
                    System.Globalization.CultureInfo.InvariantCulture);
                Theme.DrawTrackedCentered(g, shown, vf, new Rectangle(0, 0, Width, ValueH),
                    Math.Abs(value) < 0.05 ? Theme.Muted : lit, Theme.Track);
            }
            using (var lf = Theme.Ui(8f))
                Theme.DrawTrackedCentered(g, Label, lf,
                    new Rectangle(0, Height - LabelH, Width, LabelH), Theme.Muted, Theme.Track);
        }
    }

    /// <summary>
    /// The analyser. Spectrum or oscilloscope, click to change — the same two the
    /// engine can feed and the only decoration in the app.
    /// </summary>
    class Analyser : Themed
    {
        // Wide enough to read as a spectrum, few enough that each column is
        // still a column rather than a hairline.
        public const int Bars = 36;

        readonly float[] levels = new float[Bars];
        readonly float[] peaks = new float[Bars];
        readonly float[] re = new float[AudioEngine.AnalyserSize];
        readonly float[] im = new float[AudioEngine.AnalyserSize];
        readonly float[] window = Fft.Hann(AudioEngine.AnalyserSize);
        readonly float[] samples = new float[AudioEngine.AnalyserSize];
        bool haveSamples;

        // Deflection per output channel, already mapped onto the printed scale by
        // the engine — the ballistics live there, with the samples. The peak hold
        // is this control's own: it is a property of the display, not of the mix.
        float vuLeft, vuRight, holdLeft, holdRight;

        public AnalyserMode Mode = AnalyserMode.Vu;
        public event EventHandler ModeChanged;

        public Analyser()
        {
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Mode = Mode == AnalyserMode.Vu ? AnalyserMode.Bars
                     : Mode == AnalyserMode.Bars ? AnalyserMode.Scope
                     : Mode == AnalyserMode.Scope ? AnalyserMode.Off : AnalyserMode.Vu;
                Invalidate();
                EventHandler h = ModeChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }

        /// <summary>Takes one window from the engine and folds it into the bars.
        /// True when something moved, so a settled display can skip its
        /// repaint.</summary>
        public bool Feed(AudioEngine engine, bool playing)
        {
            if (Mode == AnalyserMode.Off) return false;

            if (Mode == AnalyserMode.Vu)
            {
                float l = 0, r = 0;
                if (engine != null) engine.Levels(out l, out r);
                bool moved = Math.Abs(l - vuLeft) > 0.002f || Math.Abs(r - vuRight) > 0.002f;
                vuLeft = l;
                vuRight = r;
                // The hold sits at the loudest recent peak and slides back down,
                // which is what makes a transient readable at all: the level
                // itself is past before the eye finds it.
                holdLeft = Hold(holdLeft, l, ref moved);
                holdRight = Hold(holdRight, r, ref moved);
                return moved || playing;
            }

            haveSamples = engine != null && engine.CopyAnalyser(samples);
            if (Mode == AnalyserMode.Scope) return haveSamples || playing;

            if (!haveSamples)
            {
                // Let the bars fall rather than blanking them: stopping should look
                // like the display settling, not like it crashed.
                bool moving = false;
                for (int i = 0; i < Bars; i++)
                {
                    if (levels[i] > 0.002f) { levels[i] *= 0.78f; moving = true; }
                    else levels[i] = 0;
                    if (peaks[i] > 0.002f) { peaks[i] = Math.Max(0, peaks[i] - 0.02f); moving = true; }
                    else peaks[i] = 0;
                }
                return moving;
            }

            int n = AudioEngine.AnalyserSize;
            for (int i = 0; i < n; i++) { re[i] = samples[i] * window[i]; im[i] = 0; }
            Fft.Forward(re, im);

            int bins = n / 2;
            for (int b = 0; b < Bars; b++)
            {
                // Logarithmic band edges. Split the spectrum linearly and most of
                // the bars land above 5 kHz, where music has almost no energy, and
                // the display sits flat on the right for every track.
                int from = (int)Math.Floor(Math.Pow(bins, b / (double)Bars));
                int to = (int)Math.Floor(Math.Pow(bins, (b + 1) / (double)Bars));
                if (to <= from) to = from + 1;
                if (to > bins) to = bins;

                double sum = 0;
                for (int i = from; i < to; i++)
                    sum += Math.Sqrt(re[i] * (double)re[i] + im[i] * (double)im[i]);
                double mag = sum / (to - from);

                // Onto decibels: linear magnitudes leave everything in the bottom
                // tenth of the panel.
                double db = 20 * Math.Log10(mag + 1e-9);
                float v = (float)((db + 60) / 60.0);
                if (v < 0) v = 0;
                if (v > 1) v = 1;

                levels[b] = v > levels[b] ? v : levels[b] * 0.74f + v * 0.26f;
                peaks[b] = levels[b] > peaks[b] ? levels[b] : Math.Max(0, peaks[b] - 0.014f);
            }
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Surface(this, g, Theme.Sunken);
            g.SmoothingMode = SmoothingMode.None;
            var inner = new Rectangle(6, 6, Width - 12, Height - 12);
            if (inner.Width <= 0 || inner.Height <= 0) return;

            if (Mode == AnalyserMode.Off)
            {
                using (var f = Theme.Ui(9f))
                    Theme.DrawTrackedCentered(g, Lang.T("vis.off"), f,
                        new Rectangle(0, 0, Width, Height), Theme.Line, Theme.Track);
                return;
            }

            if (Mode == AnalyserMode.Vu) { PaintVu(g, inner); return; }
            if (Mode == AnalyserMode.Scope) { PaintScope(g, inner); return; }

            // Discrete cells rather than a continuous bar: a phosphor display is a
            // grid of lamps, and the stepping is most of what makes it read as one.
            const int cell = 4;
            int rows = Math.Max(1, inner.Height / cell);
            float bw = inner.Width / (float)Bars;
            float w = Math.Max(1f, bw - 2f);

            using (var dim = new SolidBrush(Color.FromArgb(18, Theme.Text)))
            using (var lit = new SolidBrush(Theme.Text))
            using (var hot = new SolidBrush(Theme.Bright))
                for (int i = 0; i < Bars; i++)
                {
                    float x = inner.X + i * bw;
                    int on = (int)(levels[i] * rows);
                    int peak = (int)(peaks[i] * rows);
                    for (int r = 0; r < rows; r++)
                    {
                        float y = inner.Bottom - (r + 1) * cell;
                        Brush b = r == peak - 1 ? hot : (r < on ? lit : dim);
                        g.FillRectangle(b, x, y, w, cell - 1);
                    }
                }
        }

        // ---- The output level meter ---------------------------------------------

        /// <summary>How many segments each channel gets.</summary>
        const int VuSegments = 20;
        /// <summary>Where the segments stop being the scheme's own colour: amber
        /// from here, red from RedFrom. Those two are fixed rather than derived,
        /// because "approaching clipping" has to mean the same thing in every
        /// scheme.</summary>
        const float AmberFrom = 0.62f, RedFrom = 0.84f;

        static float Hold(float current, float level, ref bool moved)
        {
            float next = level > current ? level : Math.Max(0f, current - 0.012f);
            if (Math.Abs(next - current) > 0.002f) moved = true;
            return next;
        }

        static Color SegmentColour(int index)
        {
            float f = index / (float)(VuSegments - 1);
            if (f >= RedFrom) return Theme.Danger;
            if (f >= AmberFrom) return Theme.Warn;
            return Theme.Text;
        }

        /// <summary>
        /// One row of segments per output, the way a deck carries a meter per
        /// channel. Discrete cells rather than a continuous bar: the stepping is
        /// most of what makes it read as a meter and not as a progress bar.
        /// </summary>
        void PaintVu(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.None;

            const int LabelW = 16, RowH = 18, Gap = 8, ScaleH = 12;
            int rowsTop = r.Y + Math.Max(0, (r.Height - (RowH * 2 + Gap + ScaleH)) / 2);

            PaintChannel(g, r, rowsTop, LabelW, RowH, "L", vuLeft, holdLeft);
            PaintChannel(g, r, rowsTop + RowH + Gap, LabelW, RowH, "R", vuRight, holdRight);
            PaintScale(g, r, rowsTop + RowH * 2 + Gap + 2, LabelW);
        }

        void PaintChannel(Graphics g, Rectangle r, int y, int labelW, int rowH,
            string channel, float level, float hold)
        {
            using (var f = Theme.UiBold(8f))
                Theme.DrawTrackedLeft(g, channel, f, new Rectangle(r.X, y, labelW, rowH),
                    Theme.Muted, Theme.Track);

            int x0 = r.X + labelW;
            int span = r.Right - x0;
            float step = span / (float)VuSegments;
            float w = Math.Max(2f, step - 2f);

            int lit = (int)Math.Round(level * VuSegments);
            int peak = hold > 0.002f
                ? Math.Min(VuSegments - 1, (int)Math.Round(hold * VuSegments) - 1) : -1;

            for (int i = 0; i < VuSegments; i++)
            {
                Color c = SegmentColour(i);
                bool on = i < lit;
                // An unlit segment still shows faintly: a meter you can see the
                // whole of reads as an instrument, and one that vanishes reads as
                // a bar that has not been drawn yet.
                Color shown = i == peak ? Theme.Bright
                            : on ? c : Color.FromArgb(34, c);
                using (var b = new SolidBrush(shown))
                    g.FillRectangle(b, x0 + i * step, y + 2, w, rowH - 4);
            }
        }

        /// <summary>The dB legend under the two rows, marked where a real meter
        /// prints it.</summary>
        void PaintScale(Graphics g, Rectangle r, int y, int labelW)
        {
            var marks = new[] { 0f, 0.42f, 0.62f, 0.78f, 1f };
            var captions = new[] { "-20", "-10", "-5", "0", "+3" };
            int x0 = r.X + labelW;
            int span = r.Right - x0;

            using (var f = Theme.Ui(7.5f))
                for (int i = 0; i < marks.Length; i++)
                {
                    int w = Theme.MeasureTracked(captions[i], f, Theme.Track);
                    int at = x0 + (int)(span * marks[i]);
                    // The first and last are pulled inside the ends rather than
                    // centred on them, or they hang off the card.
                    int left = i == 0 ? at : (i == marks.Length - 1 ? at - w : at - w / 2);
                    Theme.DrawTracked(g, captions[i], f, new Point(left, y),
                        marks[i] >= RedFrom ? Theme.Danger
                            : marks[i] >= AmberFrom ? Theme.Warn : Theme.Line,
                        Theme.Track);
                }
        }

        void PaintScope(Graphics g, Rectangle r)
        {
            using (var pen = new Pen(Theme.Line))
            {
                pen.DashStyle = DashStyle.Dot;
                g.DrawLine(pen, r.X, r.Y + r.Height / 2, r.Right, r.Y + r.Height / 2);
            }
            if (!haveSamples) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            int n = samples.Length;
            var pts = new PointF[r.Width];
            for (int x = 0; x < r.Width; x++)
            {
                int from = (int)((long)x * n / r.Width);
                int to = (int)((long)(x + 1) * n / r.Width);
                if (to <= from) to = from + 1;
                if (to > n) to = n;
                // The extreme in the column, not the average: averaging a waveform
                // down to a few hundred columns flattens anything above a few
                // hundred hertz into a straight line.
                float extreme = 0;
                for (int i = from; i < to; i++)
                    if (Math.Abs(samples[i]) > Math.Abs(extreme)) extreme = samples[i];
                float v = extreme * 1.8f;
                if (v > 1) v = 1;
                if (v < -1) v = -1;
                pts[x] = new PointF(r.X + x, r.Y + r.Height / 2f - v * (r.Height / 2f - 2));
            }
            using (var pen = new Pen(Theme.Text, 1.4f)) g.DrawLines(pen, pts);
        }
    }

    /// <summary>What the output display is showing. The level meter is first
    /// because it is the default: a row of segments per output is what the deck
    /// this imitates carries.</summary>
    enum AnalyserMode { Vu, Bars, Scope, Off }

    /// <summary>
    /// The playlist. Hand-drawn rather than a ListView: the stock control's
    /// scrollbar cannot be themed, its owner-draw path has the sub-item repaint
    /// trap, and none of what it offers beyond drawing rows is wanted here.
    /// </summary>
    class TrackList : Themed
    {
        public const int RowH = 26;
        const int ScrollW = 10;

        Playlist list;
        readonly HashSet<int> selected = new HashSet<int>();
        int anchor = -1;
        int top;
        int hover = -1;
        bool draggingScroll;
        int scrollGrabY, scrollGrabTop;

        public event EventHandler<int> Activated;
        public event EventHandler SelectionChanged;

        public TrackList()
        {
            TabStop = true;
            BackColor = Theme.Sunken;
        }

        public Playlist Source
        {
            get { return list; }
            set { list = value; top = 0; selected.Clear(); Invalidate(); }
        }

        public int SelectionCount { get { return selected.Count; } }
        public IEnumerable<int> Selection { get { return selected; } }

        int Count { get { return list != null ? list.Count : 0; } }
        int VisibleRows { get { return Math.Max(1, Height / RowH); } }
        int MaxTop { get { return Math.Max(0, Count - VisibleRows); } }
        bool NeedsScroll { get { return Count > VisibleRows; } }
        int ListWidth { get { return NeedsScroll ? Width - ScrollW : Width; } }

        public void ClearSelection()
        {
            if (selected.Count == 0) return;
            selected.Clear();
            anchor = -1;
            RaiseSelection();
            Invalidate();
        }

        void RaiseSelection()
        {
            EventHandler h = SelectionChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        /// <summary>Brings a row into view. It matters most with shuffle on: the
        /// next track can be anywhere, and without this the highlight simply leaves
        /// the screen.</summary>
        public void EnsureVisible(int index)
        {
            if (index < 0 || index >= Count) return;
            if (index < top) top = index;
            else if (index >= top + VisibleRows) top = index - VisibleRows + 1;
            Clamp();
            Invalidate();
        }

        void Clamp()
        {
            if (top > MaxTop) top = MaxTop;
            if (top < 0) top = 0;
        }

        /// <summary>Called after the owner changes the list: a selection is
        /// positional, and anything past the end is meaningless.</summary>
        public void Reset()
        {
            var stale = new List<int>();
            foreach (int i in selected) if (i >= Count) stale.Add(i);
            foreach (int i in stale) selected.Remove(i);
            Clamp();
            Invalidate();
        }

        // ---- Mouse ---------------------------------------------------------------

        int RowAt(int y)
        {
            int index = top + y / RowH;
            return index >= 0 && index < Count ? index : -1;
        }

        Rectangle ScrollThumb()
        {
            if (!NeedsScroll) return Rectangle.Empty;
            int h = Math.Max(24, (int)((long)Height * VisibleRows / Count));
            int y = MaxTop == 0 ? 0 : (int)((long)(Height - h) * top / MaxTop);
            return new Rectangle(Width - ScrollW, y, ScrollW, h);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (NeedsScroll && e.X >= Width - ScrollW)
            {
                Rectangle thumb = ScrollThumb();
                if (thumb.Contains(e.Location))
                {
                    draggingScroll = true;
                    scrollGrabY = e.Y;
                    scrollGrabTop = top;
                    Capture = true;
                }
                else
                {
                    top = e.Y < thumb.Y ? top - VisibleRows : top + VisibleRows;
                    Clamp();
                    Invalidate();
                }
                base.OnMouseDown(e);
                return;
            }

            int index = RowAt(e.Y);
            if (index < 0)
            {
                if (e.Button == MouseButtons.Left) ClearSelection();
                base.OnMouseDown(e);
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                // A right-click outside the selection moves it there first, so a
                // context action cannot act on rows that are not shown as chosen.
                if (!selected.Contains(index))
                {
                    selected.Clear();
                    selected.Add(index);
                    anchor = index;
                    RaiseSelection();
                }
                Invalidate();
                base.OnMouseDown(e);
                return;
            }

            if ((ModifierKeys & Keys.Shift) != 0 && anchor >= 0)
            {
                selected.Clear();
                int from = Math.Min(anchor, index), to = Math.Max(anchor, index);
                for (int i = from; i <= to; i++) selected.Add(i);
            }
            else if ((ModifierKeys & Keys.Control) != 0)
            {
                if (!selected.Remove(index)) selected.Add(index);
                anchor = index;
            }
            else
            {
                selected.Clear();
                selected.Add(index);
                anchor = index;
            }
            RaiseSelection();
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (draggingScroll)
            {
                Rectangle thumb = ScrollThumb();
                int span = Math.Max(1, Height - thumb.Height);
                top = scrollGrabTop + (int)((long)(e.Y - scrollGrabY) * MaxTop / span);
                Clamp();
                Invalidate();
                base.OnMouseMove(e);
                return;
            }
            int index = NeedsScroll && e.X >= Width - ScrollW ? -1 : RowAt(e.Y);
            if (index != hover) { hover = index; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            draggingScroll = false; Capture = false; base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover != -1) { hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (NeedsScroll && e.X >= Width - ScrollW) return;
            int index = RowAt(e.Y);
            if (index < 0) return;
            EventHandler<int> h = Activated;
            if (h != null) h(this, index);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines <= 0) lines = 3;
            top -= Math.Sign(e.Delta) * lines;
            Clamp();
            Invalidate();
        }

        // ---- Keyboard -------------------------------------------------------------

        protected override bool IsInputKey(Keys key)
        {
            // Without this the arrows and Home/End go to the form's tab handling
            // and never reach OnKeyDown.
            switch (key)
            {
                case Keys.Up: case Keys.Down: case Keys.Home:
                case Keys.End: case Keys.PageUp: case Keys.PageDown:
                    return true;
                default: return base.IsInputKey(key);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Count == 0) { base.OnKeyDown(e); return; }
            int cursor = anchor >= 0 ? anchor : top;
            int move;
            switch (e.KeyCode)
            {
                case Keys.Up: move = -1; break;
                case Keys.Down: move = 1; break;
                case Keys.PageUp: move = -VisibleRows; break;
                case Keys.PageDown: move = VisibleRows; break;
                case Keys.Home: move = -Count; break;
                case Keys.End: move = Count; break;
                case Keys.Enter:
                    if (anchor >= 0)
                    {
                        EventHandler<int> h = Activated;
                        if (h != null) h(this, anchor);
                    }
                    e.Handled = true;
                    return;
                case Keys.A:
                    if (e.Control)
                    {
                        selected.Clear();
                        for (int i = 0; i < Count; i++) selected.Add(i);
                        RaiseSelection();
                        Invalidate();
                        e.Handled = true;
                    }
                    return;
                default:
                    base.OnKeyDown(e);
                    return;
            }

            int target = cursor + move;
            if (target < 0) target = 0;
            if (target >= Count) target = Count - 1;

            if (e.Shift && anchor >= 0)
            {
                selected.Clear();
                int from = Math.Min(anchor, target), to = Math.Max(anchor, target);
                for (int i = from; i <= to; i++) selected.Add(i);
                // The anchor stays put: that is what lets a range be widened and
                // narrowed without it re-anchoring on every keystroke.
            }
            else
            {
                selected.Clear();
                selected.Add(target);
                anchor = target;
            }
            EnsureVisible(target);
            RaiseSelection();
            e.Handled = true;
        }

        // ---- Paint ------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Surface(this, g, Theme.Sunken);
            if (Count == 0) { PaintEmpty(g); return; }

            Clamp();
            int listW = ListWidth;
            int current = list.CurrentIndex;
            int last = Math.Min(Count, top + VisibleRows + 1);

            using (var mono = Theme.Ui(9.5f))
            using (var monoBold = Theme.UiBold(9.5f))
            using (var num = Theme.Ui(8.5f))
                for (int i = top; i < last; i++)
                {
                    Track t = list.At(i);
                    if (t == null) continue;
                    var row = new Rectangle(0, (i - top) * RowH, listW, RowH);

                    bool isSelected = selected.Contains(i);
                    bool isCurrent = i == current;

                    if (isSelected)
                        using (var b = new SolidBrush(Theme.Subtle)) g.FillRectangle(b, row);
                    else if (i == hover)
                        using (var b = new SolidBrush(Color.FromArgb(20, Theme.Text)))
                            g.FillRectangle(b, row);

                    // The playing row is marked down its left edge as well as by
                    // colour: with a selection over it, colour alone is not enough
                    // to tell which row is which.
                    if (isCurrent)
                    {
                        using (var b = new SolidBrush(Color.FromArgb(26, Theme.Text)))
                            g.FillRectangle(b, row);
                        using (var b = new SolidBrush(Theme.Text))
                            g.FillRectangle(b, 0, row.Y + 3, 3, row.Height - 6);
                    }

                    Color ink = isCurrent ? Theme.Bright : (isSelected ? Theme.Text : Theme.Muted);
                    Font font = isCurrent ? monoBold : mono;

                    const int numW = 40, pad = 12;
                    string index = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    Theme.Draw(g, index, num, new Rectangle(pad, row.Y, numW, RowH),
                        isCurrent ? Theme.Text : Theme.Line);

                    string time = t.Duration > 0 ? Util.Time(t.Duration) : "--:--";
                    int timeW = Theme.Measure(time, mono) + 8;
                    Theme.Draw(g, time, mono,
                        new Rectangle(listW - timeW - pad, row.Y, timeW, RowH),
                        isCurrent ? Theme.Text : Theme.Line);

                    int labelX = pad + numW;
                    Theme.Draw(g, t.Label, font,
                        new Rectangle(labelX, row.Y, listW - labelX - timeW - pad * 2, RowH), ink);
                }

            PaintScrollbar(g);
        }

        void PaintEmpty(Graphics g)
        {
            float cx = Width / 2f, cy = Height / 2f;
            Ico.List(g, new RectangleF(cx - 26, cy - 66, 52, 52), Theme.Line);
            using (var f = Theme.UiBold(11f))
                Theme.DrawTrackedCentered(g, Lang.T("list.empty").ToUpperInvariant(), f,
                    new Rectangle(0, (int)cy - 10, Width, 26), Theme.Muted, Theme.Track);
            using (var f = Theme.Ui(9f))
                Theme.DrawTrackedCentered(g, Lang.T("list.formats"), f,
                    new Rectangle(0, (int)cy + 18, Width, 22), Theme.Line, Theme.Track);
        }

        void PaintScrollbar(Graphics g)
        {
            if (!NeedsScroll) return;
            var track = new Rectangle(Width - ScrollW, 0, ScrollW, Height);
            using (var b = new SolidBrush(Color.FromArgb(0, 0, 0))) g.FillRectangle(b, track);
            Rectangle thumb = ScrollThumb();
            thumb.Inflate(-2, 0);
            using (var b = new SolidBrush(Theme.Line)) g.FillRectangle(b, thumb);
        }
    }
}
