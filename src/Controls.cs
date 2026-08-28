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

    /// <summary>A page or bar background: a flat slab of the window ground with
    /// an optional hairline rule along one edge. A plain Panel would do, but this
    /// one follows the scheme and repaints itself when it changes.</summary>
    class Ground : Themed
    {
        public Color Back = Theme.Bg;
        public bool RuleBottom, RuleTop;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Surface(this, g, Back);
            using (var pen = new Pen(Theme.CardLine))
            {
                if (RuleTop) g.DrawLine(pen, 0, 0, Width, 0);
                if (RuleBottom) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }
            // The base implementation raises the Paint event, and it has to run
            // after the ground is laid down: the header's wordmark and the status
            // line are attached that way, and without this call neither appears.
            base.OnPaint(e);
        }
    }

    /// <summary>A card: the rounded slab with its shadow, and an uppercase
    /// header along the top.</summary>
    class Card : Themed
    {
        public string Header = "";
        /// <summary>Drawn beside the header — a count, a total, a state
        /// word.</summary>
        public string Note = "";

        public const int HeaderH = 36;

        public Card(string header)
        {
            Header = header;
            Padding = new Padding(16, HeaderH + 6, 16, 14);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Surface(this, g, Parent != null ? Parent.BackColor : Theme.Bg);
            Theme.PaintCard(g, Width, Height);

            int noteX = 18;
            if (Header.Length > 0)
                using (var f = Theme.UiBold(8.5f))
                {
                    string caption = Header.ToUpperInvariant();
                    Theme.DrawLabel(g, caption, f, new Point(18, 15), Theme.Muted);
                    noteX += Theme.Measure(caption, f) + 16;
                }
            if (Note.Length > 0)
                using (var f = Theme.Ui(8.5f))
                    Theme.DrawLabel(g, Note, f, new Point(noteX, 15), Theme.Disabled);

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
            Theme.Surface(this, g, Parent != null ? Parent.BackColor : Theme.Bg);
            Theme.PaintCard(g, Width, Height);

            using (var capF = Theme.UiBold(8f))
            using (var valF = Theme.UiBold(13f))
            {
                int x = 22;
                for (int i = 0; i < Captions.Length; i++)
                {
                    string cap = Captions[i].ToUpperInvariant();
                    string val = i < Values.Length ? Values[i] : "";
                    int cell = Math.Max(Theme.Measure(cap, capF), Theme.Measure(val, valF));
                    Theme.DrawLabel(g, cap, capF, new Point(x, 13), Theme.Muted);
                    Theme.Draw(g, val, valF, new Rectangle(x, 28, cell + 8, 24), Theme.Text);
                    x += cell + 30;
                    if (x > Width - 24) break;
                }
            }
        }
    }

    /// <summary>A tab in the header. The active one is a filled pill, which is
    /// the idiom every current dashboard uses and the one the tools next door
    /// wear.</summary>
    class NavTab : Themed
    {
        public IconDraw Icon;
        public bool Active;
        bool over;

        public NavTab(string text, IconDraw icon)
        {
            Text = text;
            Icon = icon;
            Height = 38;
            Cursor = Cursors.Hand;
            Font = Theme.UiBold(9.5f);
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
            using (var f = Theme.UiBold(9.5f))
                // 46 is the chrome the paint below uses — 34 before the label and
                // 12 after — plus slack for the padding TextRenderer adds on a
                // draw but not on a NoPadding measure. One pixel short and
                // EndEllipsis does not trim a character, it trims three.
                Width = 46 + Theme.Measure(Text, f);
            // OnTextChanged does not repaint a UserPaint control: only ResizeRedraw
            // invalidates, and a translated caption can measure to the same width,
            // in which case the resize never happens and the tab keeps painting
            // the old language.
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Surface(this, g, Theme.Bg);
            var pill = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            if (Active) Theme.Fill(g, pill, Height / 2f, Theme.AccentSoft);
            else if (over) Theme.Fill(g, pill, Height / 2f, Color.FromArgb(13, 255, 255, 255));

            Color ink = Active ? Theme.AccentHot : (over ? Theme.Text : Theme.Muted);
            if (Icon != null) Icon(g, new RectangleF(13, (Height - 17) / 2f, 17, 17), ink);
            using (var f = Theme.UiBold(9.5f))
                Theme.Draw(g, Text, f, new Rectangle(34, 0, Width - 44, Height), ink);
        }
    }

    /// <summary>
    /// A push button: a rounded slab that sits on a card. The latched variant
    /// fills with the accent, which is how the scheme, the language and the
    /// equaliser switch say which one is chosen.
    /// </summary>
    class Btn : Themed, IButtonControl
    {
        public IconDraw Icon;
        public bool Latch, On;
        /// <summary>The one action a card is really about: filled accent whether
        /// or not it latches.</summary>
        public bool Primary;

        bool over, down;
        DialogResult dialogResult = DialogResult.None;

        public Btn(string text)
        {
            Text = text;
            Height = 32;
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
                Width = Math.Max(minimum, (Icon != null ? 26 : 0) + Theme.Measure(Text, f) + 28);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Surface(this, g, Parent != null ? Parent.BackColor : Theme.Card);
            var box = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);

            bool lit = Primary || (Latch && On);
            Color fill = !Enabled ? Theme.Subtle
                       : lit ? (over ? Theme.AccentHot : Theme.Accent)
                       : (down ? Theme.Card : (over ? Theme.SubtleHot : Theme.Subtle));
            Theme.Fill(g, box, Theme.RadiusSmall + 2, fill);
            // Only the unlit buttons need an outline: a filled accent already
            // separates itself from the card, and ringing it as well is what makes
            // every control read as a cell.
            if (!lit) Theme.Outline(g, box, Theme.RadiusSmall + 2,
                !Enabled ? Theme.CardLine : (over ? Theme.AccentHot : Theme.CardLine));

            Color ink = !Enabled ? Theme.Muted : lit ? Theme.OnAccent : Theme.Text;
            using (var f = Theme.UiBold(9f))
            {
                int textW = Theme.Measure(Text, f);
                int glyph = Icon != null ? 17 : 0;
                int gap = glyph > 0 && textW > 0 ? 8 : 0;
                int x = Math.Max(8, (Width - (glyph + gap + textW)) / 2);
                if (Icon != null) Icon(g, new RectangleF(x, (Height - 17) / 2f, 17, 17), ink);
                if (textW > 0)
                    Theme.Draw(g, Text, f,
                        new Rectangle(x + glyph + gap, 0, Width - x - glyph - gap, Height), ink);
            }
        }
    }

    /// <summary>
    /// A round transport key. Separate from Btn because the transport is a row of
    /// glyphs with no captions, and it wants to stay circular whatever the
    /// language does to everything else.
    /// </summary>
    class KeyBtn : Themed, IButtonControl
    {
        public IconDraw Icon;
        /// <summary>The play key: a filled accent disc, the one control on the
        /// page the eye should land on first.</summary>
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
            var disc = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float rad = Math.Min(disc.Width, disc.Height) / 2f;

            bool latched = Latch && On;
            Color fill = !Enabled ? Theme.Subtle
                       : Primary ? (over ? Theme.AccentHot : Theme.Accent)
                       : latched ? Theme.AccentSoft
                       : (down ? Theme.Card : (over ? Theme.SubtleHot : Theme.Subtle));
            Theme.Fill(g, disc, rad, fill);
            if (!Primary)
                Theme.Outline(g, disc, rad,
                    !Enabled ? Theme.CardLine : latched ? Theme.Accent
                    : over ? Theme.AccentHot : Theme.CardLine);

            Color ink = !Enabled ? Theme.Muted
                      : Primary ? Theme.OnAccent
                      : latched ? Theme.AccentHot : Theme.Text;
            // A glyph that shifts by a pixel on the press is most of what makes a
            // flat button feel like it was pushed.
            var box = new RectangleF(0, down && Enabled ? 1 : 0, Width, Height);
            if (Icon != null) Icon(g, box, ink);
        }
    }

    /// <summary>
    /// A horizontal slider: a rounded track, a filled run in the accent and a
    /// round knob at the setting. Used for position, volume and balance, which
    /// differ only in what they are measuring.
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
            Height = 24;
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

        // The knob has to stay inside the control, so the travel is inset by its
        // radius at both ends and every position maps into that span.
        const float Knob = 13f;
        float Inset { get { return Knob / 2f + 1; } }
        float Span { get { return Math.Max(1f, Width - Inset * 2); } }

        void SetFromMouse(int x)
        {
            double f = (x - Inset) / Span;
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
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool live = over || dragging;
            float h = live ? 7f : 5f;
            float mid = Height / 2f;
            var track = new RectangleF(Inset - h / 2, mid - h / 2, Span + h, h);
            Theme.Fill(g, track, h / 2, Theme.Sunken);
            Theme.Outline(g, track, h / 2, Theme.CardLine);

            float at = Inset + Span * (float)Fraction;
            float centre = Inset + Span / 2f;
            float from = FromCentre ? Math.Min(centre, at) : Inset;
            float to = FromCentre ? Math.Max(centre, at) : at;
            if (to - from > 0.5f)
                Theme.Fill(g, new RectangleF(from - h / 2, mid - h / 2, to - from + h, h),
                    h / 2, live ? Theme.AccentHot : Theme.Accent);

            // A white disc with an accent ring: it has to be findable when the
            // fill behind it is only a pixel or two wide.
            var knob = new RectangleF(at - Knob / 2, mid - Knob / 2, Knob, Knob);
            using (var b = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                g.FillEllipse(b, knob.X, knob.Y + 1.5f, knob.Width, knob.Height);
            using (var b = new SolidBrush(live ? Color.White : Theme.Text)) g.FillEllipse(b, knob);
            using (var pen = new Pen(live ? Theme.AccentHot : Theme.Accent, 2f))
                g.DrawEllipse(pen, knob.X + 1, knob.Y + 1, knob.Width - 2, knob.Height - 2);
        }
    }

    /// <summary>
    /// A vertical fader, the kind a graphic equaliser has eleven of. The same
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

        const int LabelH = 20, ValueH = 18, KnobH = 12;
        int TravelTop { get { return ValueH + 6 + KnobH / 2; } }
        int TravelH { get { return Math.Max(1, Height - LabelH - TravelTop - 6 - KnobH / 2); } }

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
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool live = over || dragging;
            float cx = Width / 2f;
            const float w = 6f;
            var track = new RectangleF(cx - w / 2, TravelTop - w / 2, w, TravelH + w);
            Theme.Fill(g, track, w / 2, Theme.Sunken);
            Theme.Outline(g, track, w / 2, Theme.CardLine);

            float zero = TravelTop + TravelH * (float)(Max / (Max - Min));
            float at = TravelTop + TravelH * (float)((Max - value) / (Max - Min));
            if (Math.Abs(at - zero) > 0.5f)
                Theme.Fill(g, new RectangleF(cx - w / 2, Math.Min(at, zero) - w / 2, w,
                    Math.Abs(zero - at) + w), w / 2, live ? Theme.AccentHot : Theme.Accent);

            // The knob is a pill rather than a disc: a fader is grabbed by its
            // width, and a round one on a 6px track reads as a bead on a string.
            var knob = new RectangleF(cx - 11, at - KnobH / 2f, 22, KnobH);
            using (var b = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
            using (var p = Theme.Round(new RectangleF(knob.X, knob.Y + 1.5f, knob.Width, knob.Height),
                       KnobH / 2f))
                g.FillPath(b, p);
            Theme.Fill(g, knob, KnobH / 2f, live ? Color.White : Theme.Text);
            Theme.Outline(g, knob, KnobH / 2f, live ? Theme.AccentHot : Theme.Accent);

            using (var vf = Theme.UiBold(8.5f))
            {
                string shown = (value > 0 ? "+" : "") + value.ToString("0.#",
                    System.Globalization.CultureInfo.InvariantCulture);
                Theme.DrawCentered(g, shown, vf, new Rectangle(0, 0, Width, ValueH),
                    Math.Abs(value) < 0.05 ? Theme.Muted : (live ? Theme.AccentHot : Theme.Text));
            }
            using (var lf = Theme.Ui(8f))
                Theme.DrawCentered(g, Label, lf,
                    new Rectangle(0, Height - LabelH, Width, LabelH), Theme.Muted);
        }
    }

    /// <summary>
    /// The analyser. Level meter, spectrum or oscilloscope, click to change — the
    /// same three the engine can feed and the only decoration in the app.
    /// </summary>
    class Analyser : Themed
    {
        // Wide enough to read as a spectrum, few enough that each column is
        // still a column rather than a hairline.
        public const int Bars = 32;

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
            Theme.Surface(this, g, Theme.Card);
            var well = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.Fill(g, well, Theme.RadiusSmall, Theme.Sunken);

            var inner = new Rectangle(10, 8, Width - 20, Height - 16);
            if (inner.Width <= 0 || inner.Height <= 0) return;

            if (Mode == AnalyserMode.Off)
            {
                using (var f = Theme.Ui(9f))
                    Theme.DrawCentered(g, Lang.T("vis.off"), f,
                        new Rectangle(0, 0, Width, Height), Theme.Disabled);
                return;
            }

            if (Mode == AnalyserMode.Vu) { PaintVu(g, inner); return; }
            if (Mode == AnalyserMode.Scope) { PaintScope(g, inner); return; }
            PaintBars(g, inner);
        }

        /// <summary>The spectrum: a column per band, rounded at the top, with the
        /// peak hold floating above it as a thin cap.</summary>
        void PaintBars(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float step = r.Width / (float)Bars;
            float w = Math.Max(2f, step - 3f);
            float rad = Math.Min(2.5f, w / 2f);

            using (var track = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
            using (var fill = new LinearGradientBrush(
                       new RectangleF(r.X, r.Y, 1, r.Height),
                       Theme.AccentHot, Theme.Accent, LinearGradientMode.Vertical))
            using (var cap = new SolidBrush(Theme.Text))
                for (int i = 0; i < Bars; i++)
                {
                    float x = r.X + i * step + (step - w) / 2f;
                    using (var p = Theme.Round(new RectangleF(x, r.Y, w, r.Height), rad))
                        g.FillPath(track, p);

                    float h = Math.Max(0, levels[i]) * r.Height;
                    if (h > 1)
                        using (var p = Theme.Round(new RectangleF(x, r.Bottom - h, w, h), rad))
                            g.FillPath(fill, p);

                    float peak = Math.Max(0, peaks[i]) * r.Height;
                    if (peak > 2)
                        using (var p = Theme.Round(
                                   new RectangleF(x, r.Bottom - peak - 2, w, 2.5f), 1.2f))
                            g.FillPath(cap, p);
                }
        }

        // ---- The output level meter ---------------------------------------------

        /// <summary>How many segments each channel gets.</summary>
        const int VuSegments = 22;
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
            return Theme.Accent;
        }

        /// <summary>
        /// One row of segments per output, the way a deck carries a meter per
        /// channel. Discrete cells rather than a continuous bar: the stepping is
        /// most of what makes it read as a meter and not as a progress bar.
        /// </summary>
        void PaintVu(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            const int LabelW = 16, RowH = 18, Gap = 10, ScaleH = 14;
            int rowsTop = r.Y + Math.Max(0, (r.Height - (RowH * 2 + Gap + ScaleH)) / 2);

            PaintChannel(g, r, rowsTop, LabelW, RowH, "L", vuLeft, holdLeft);
            PaintChannel(g, r, rowsTop + RowH + Gap, LabelW, RowH, "R", vuRight, holdRight);
            PaintScale(g, r, rowsTop + RowH * 2 + Gap + 4, LabelW);
        }

        void PaintChannel(Graphics g, Rectangle r, int y, int labelW, int rowH,
            string channel, float level, float hold)
        {
            using (var f = Theme.UiBold(8f))
                Theme.Draw(g, channel, f, new Rectangle(r.X, y, labelW, rowH), Theme.Muted);

            int x0 = r.X + labelW;
            int span = r.Right - x0;
            float step = span / (float)VuSegments;
            float w = Math.Max(2f, step - 2.5f);

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
                Color shown = i == peak ? Theme.Text
                            : on ? c : Color.FromArgb(38, c);
                Theme.Fill(g, new RectangleF(x0 + i * step, y + 2, w, rowH - 4), 2f, shown);
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

            using (var f = Theme.Ui(7f))
                for (int i = 0; i < marks.Length; i++)
                {
                    int w = Theme.Measure(captions[i], f);
                    int at = x0 + (int)(span * marks[i]);
                    // The first and last are pulled inside the ends rather than
                    // centred on them, or they hang off the card.
                    int left = i == 0 ? at : (i == marks.Length - 1 ? at - w : at - w / 2);
                    Theme.DrawLabel(g, captions[i], f, new Point(left, y),
                        marks[i] >= RedFrom ? Theme.Danger
                            : marks[i] >= AmberFrom ? Theme.Warn : Theme.Disabled);
                }
        }

        void PaintScope(Graphics g, Rectangle r)
        {
            using (var pen = new Pen(Theme.CardLine))
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
            // A wide translucent pass under the line is what gives the trace the
            // glow a lit display has, at a fraction of the cost of drawing one.
            using (var glow = new Pen(Color.FromArgb(60, Theme.Accent), 4f))
                g.DrawLines(glow, pts);
            using (var pen = new Pen(Theme.AccentHot, 1.6f)) g.DrawLines(pen, pts);
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
            int h = Math.Max(28, (int)((long)Height * VisibleRows / Count));
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

            using (var face = Theme.Ui(9.5f))
                using (var faceBold = Theme.UiBold(9.5f))
                using (var num = Theme.Digits(8f))
                using (var clock = Theme.Digits(9f))
                    for (int i = top; i < last; i++)
                    {
                        Track t = list.At(i);
                        if (t == null) continue;
                        var row = new RectangleF(3, (i - top) * RowH + 1, listW - 6, RowH - 2);

                        bool isSelected = selected.Contains(i);
                        bool isCurrent = i == current;

                        if (isSelected) Theme.Fill(g, row, Theme.RadiusSmall, Theme.Subtle);
                        else if (i == hover)
                            Theme.Fill(g, row, Theme.RadiusSmall, Color.FromArgb(14, 255, 255, 255));

                        // The playing row is marked down its left edge as well as
                        // by colour: with a selection over it, colour alone is not
                        // enough to tell which row is which.
                        if (isCurrent)
                        {
                            Theme.Fill(g, row, Theme.RadiusSmall, Theme.AccentSoft);
                            Theme.Fill(g, new RectangleF(row.X, row.Y + 4, 3, row.Height - 8),
                                1.5f, Theme.Accent);
                        }

                        Color ink = isCurrent ? Theme.Text : (isSelected ? Theme.Text : Theme.Muted);
                        Font font = isCurrent ? faceBold : face;
                        int y = (int)row.Y;

                        const int numW = 34, pad = 14;
                        string index = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        Theme.Draw(g, index, num, new Rectangle(pad, y, numW, RowH - 2),
                            isCurrent ? Theme.AccentHot : Theme.Disabled);

                        string time = t.Duration > 0 ? Util.Time(t.Duration) : "--:--";
                        int timeW = Theme.Measure(time, clock) + 6;
                        Theme.DrawRight(g, time, clock,
                            new Rectangle(listW - timeW - pad, y, timeW, RowH - 2),
                            isCurrent ? Theme.Text : Theme.Disabled);

                        int labelX = pad + numW;
                        Theme.Draw(g, t.Label, font,
                            new Rectangle(labelX, y, listW - labelX - timeW - pad * 2, RowH - 2), ink);
                    }

            PaintScrollbar(g);
        }

        void PaintEmpty(Graphics g)
        {
            float cx = Width / 2f, cy = Height / 2f;
            Ico.List(g, new RectangleF(cx - 24, cy - 62, 48, 48), Theme.CardLine);
            using (var f = Theme.UiBold(11f))
                Theme.DrawCentered(g, Lang.T("list.empty"),
                    f, new Rectangle(0, (int)cy - 8, Width, 26), Theme.Muted);
            using (var f = Theme.Ui(9f))
                Theme.DrawCentered(g, Lang.T("list.formats"), f,
                    new Rectangle(0, (int)cy + 20, Width, 22), Theme.Disabled);
        }

        void PaintScrollbar(Graphics g)
        {
            if (!NeedsScroll) return;
            Rectangle thumb = ScrollThumb();
            thumb.Inflate(-3, -2);
            Theme.Fill(g, thumb, 2f, Theme.Subtle);
        }
    }
}
