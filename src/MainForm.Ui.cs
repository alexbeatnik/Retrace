// The window: a header of nav tabs over three pages, and a status line.
//
// The same three pieces the sister apps use — a header, a page host, cards — so
// the family reads as one machine. Every page is a hand-placed absolute layout
// against PageW x PageH; nothing reflows, because nothing here is resizable.
//
// The player page descends from summary to detail the way their dashboards do:
// what is playing, the controls, the numbers, the analyser, and then the
// playlist filling the foot of the page — the slot they give their activity log.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Retrace
{
    partial class MainForm
    {
        // ---- Geometry -----------------------------------------------------------

        public const int WinW = 900, WinH = 720;
        const int HeaderH = 60;
        const int StatusH = 28;
        const int PageH = WinH - HeaderH - StatusH;   // 632
        const int Pad = 18;
        const int ContentW = WinW - Pad * 2;          // 864

        // ---- Chrome ---------------------------------------------------------------

        Ground header, pageHost, status;
        NavTab tabPlayer, tabEq, tabSetup;
        readonly List<NavTab> tabs = new List<NavTab>();
        string statusLine = "";
        int activePage;

        // ---- Player page -----------------------------------------------------------

        Ground pagePlayer;
        Card nowCard, transportCard, analyserCard, levelsCard, listCard;
        StatStrip stats;
        Bar position, volume, balance;
        Analyser analyser;
        KeyBtn keyPrev, keyPlay, keyStop, keyNext, keyOpen, keyShuffle, keyRepeat;
        TrackList list;
        Btn btnAdd, btnFolder, btnLoad, btnSave, btnRemove, btnClear;

        // ---- Equaliser page ---------------------------------------------------------

        Ground pageEq;
        Card eqCard, curveCard;
        Fader preampFader;
        readonly Fader[] bandFaders = new Fader[Equalizer.Bands.Length];
        Btn btnEqOn, btnEqReset, btnEqPreset;

        // ---- Settings page ----------------------------------------------------------

        Ground pageSetup;
        Card schemeCard, langCard, updatesCard, aboutCard;
        Btn btnAutoUpdate, btnCheckUpdate, btnInstall;
        readonly List<Btn> schemeButtons = new List<Btn>();
        Btn btnEn, btnUk;

        // ---- Construction -------------------------------------------------------------

        void BuildUi()
        {
            ClientSize = new Size(WinW, WinH);
            BackColor = Theme.Bg;

            BuildHeader();
            BuildStatus();

            pageHost = new Ground();
            pageHost.Dock = DockStyle.Fill;
            Controls.Add(pageHost);
            // Docked children are laid out from the highest index down, each taking
            // a bite out of what is left. The Fill control must therefore sit at
            // index 0 — at any other index it swallows the whole client area and
            // the header ends up painted underneath the pages.
            Controls.SetChildIndex(pageHost, 0);

            BuildPlayerPage();
            BuildEqPage();
            BuildSettingsPage();

            ShowPage(0);
            ApplyLanguage();

            Theme.Changed += delegate { Invalidate(true); };
        }

        void BuildHeader()
        {
            header = new Ground();
            header.Dock = DockStyle.Top;
            header.Height = HeaderH;
            header.RuleBottom = true;
            header.Paint += PaintHeader;
            Controls.Add(header);

            // Three tabs, not four: the playlist lives on the player page.
            tabPlayer = AddTab("nav.player", Ico.Speaker, 0);
            tabEq = AddTab("nav.equaliser", Ico.Equaliser, 1);
            tabSetup = AddTab("nav.settings", Ico.Gear, 2);
        }

        NavTab AddTab(string key, IconDraw icon, int index)
        {
            var tab = new NavTab(Lang.T(key), icon);
            tab.Name = key;
            tab.Top = (HeaderH - tab.Height) / 2;
            tab.Click += delegate { ShowPage(index); };
            header.Controls.Add(tab);
            tabs.Add(tab);
            return tab;
        }

        /// <summary>
        /// Positions the tabs by hand, right-aligned. A FlowLayoutPanel measures
        /// itself before ApplyLanguage has given the tabs their text and clips the
        /// first one, which is why this runs again after every language change.
        /// </summary>
        void LayoutTabs()
        {
            int x = WinW - Pad;
            for (int i = tabs.Count - 1; i >= 0; i--)
            {
                tabs[i].FitWidth();
                x -= tabs[i].Width;
                tabs[i].Left = x;
                x -= 4;
            }
        }

        void PaintHeader(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            // The app's own mark rather than a glyph: the window and the taskbar
            // icon are then visibly the same thing.
            Brand.PaintMark(g, new RectangleF(Pad, 14, 32, 32), Theme.Current);
            using (var f = Theme.UiBold(14.5f))
                Theme.DrawLabel(g, Brand.Product, f, new Point(Pad + 42, 19), Theme.Text);
        }

        void BuildStatus()
        {
            status = new Ground();
            status.Dock = DockStyle.Bottom;
            status.Height = StatusH;
            status.RuleTop = true;
            status.Paint += delegate(object s, PaintEventArgs e)
            {
                // A dot in the accent when something is playing: the one place the
                // state is visible without reading a word.
                bool live = engine != null && engine.State == PlayState.Playing;
                Theme.Fill(e.Graphics, new RectangleF(Pad, StatusH / 2f - 3, 6, 6), 3f,
                    live ? Theme.Accent : Theme.Disabled);
                using (var f = Theme.Ui(8.5f))
                    Theme.Draw(e.Graphics, statusLine, f,
                        new Rectangle(Pad + 14, 0, WinW - Pad * 2 - 14, StatusH), Theme.Muted);
            };
            Controls.Add(status);
        }

        void ShowPage(int index)
        {
            activePage = index;
            var pages = new[] { pagePlayer, pageEq, pageSetup };
            for (int i = 0; i < pages.Length; i++)
                if (pages[i] != null) pages[i].Visible = i == index;
            for (int i = 0; i < tabs.Count; i++) tabs[i].SetActive(i == index);
            if (index == 0 && list != null) list.EnsureVisible(playlist.CurrentIndex);
            UpdateStatus();
        }

        Ground NewPage()
        {
            var page = new Ground();
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            pageHost.Controls.Add(page);
            return page;
        }

        // ---- Player page ---------------------------------------------------------------

        const int NowY = 12, NowH = 156;
        const int RowY = 180, RowH = 124;
        const int TransportW = 384, AnalyserW = 254;
        const int StatsY = 316, StatsH = 60;
        const int ListY = 388;

        void BuildPlayerPage()
        {
            pagePlayer = NewPage();

            nowCard = new Card("");
            nowCard.SetBounds(Pad, NowY, ContentW, NowH);
            nowCard.Paint += PaintNowPlaying;
            pagePlayer.Controls.Add(nowCard);

            position = new Bar();
            position.Min = 0;
            position.Max = 1;
            position.SetBounds(20, 118, ContentW - 40, 24);
            position.ValueChanged += delegate { OnSeekBarMoved(); };
            nowCard.Controls.Add(position);

            // Transport, analyser and levels share one row.
            transportCard = new Card("");
            transportCard.SetBounds(Pad, RowY, TransportW, RowH);
            pagePlayer.Controls.Add(transportCard);

            int x = 16;
            keyPrev = AddKey(ref x, Ico.Previous, false, delegate { PlayPrevious(); });
            keyPlay = AddKey(ref x, Ico.Play, true, delegate { TogglePlay(); });
            keyStop = AddKey(ref x, Ico.Stop, false, delegate { StopPlayback(); });
            keyNext = AddKey(ref x, Ico.Next, false, delegate { Advance(false); });
            keyOpen = AddKey(ref x, Ico.Eject, false, delegate { AddFilesDialog(); });

            // Shuffle and repeat are the same round key, latched: at this size a
            // caption beside the glyph would not fit, and the lit state says more
            // than the word would.
            x += 10;
            keyShuffle = AddKey(ref x, Ico.Shuffle, false, delegate { ToggleShuffle(); });
            keyShuffle.Latch = true;
            keyRepeat = AddKey(ref x, Ico.RepeatAll, false, delegate { CycleRepeat(); });
            keyRepeat.Latch = true;

            int analyserX = Pad + TransportW + 12;
            analyserCard = new Card("");
            analyserCard.SetBounds(analyserX, RowY, AnalyserW, RowH);
            pagePlayer.Controls.Add(analyserCard);

            analyser = new Analyser();
            analyser.SetBounds(12, Card.HeaderH, AnalyserW - 24, RowH - Card.HeaderH - 12);
            analyser.ModeChanged += delegate { SaveSettings(); };
            analyserCard.Controls.Add(analyser);

            int levelsX = analyserX + AnalyserW + 12;
            levelsCard = new Card("");
            levelsCard.SetBounds(levelsX, RowY, Pad + ContentW - levelsX, RowH);
            levelsCard.Paint += PaintLevels;
            pagePlayer.Controls.Add(levelsCard);

            volume = new Bar();
            volume.SetBounds(16, 56, levelsCard.Width - 32, 22);
            volume.ValueChanged += delegate { OnVolumeMoved(); };
            levelsCard.Controls.Add(volume);

            balance = new Bar();
            balance.Min = -1;
            balance.Max = 1;
            balance.FromCentre = true;
            balance.Detent = 0.05;
            balance.SetBounds(16, 94, levelsCard.Width - 32, 22);
            balance.ValueChanged += delegate { OnBalanceMoved(); };
            levelsCard.Controls.Add(balance);

            stats = new StatStrip();
            stats.SetBounds(Pad, StatsY, ContentW, StatsH);
            pagePlayer.Controls.Add(stats);

            BuildListCard();
        }

        /// <summary>
        /// The playlist, in the slot the sister apps give their activity log: a
        /// full-width card at the foot of the page with its actions along the
        /// header row rather than under the list, which is what keeps the rows the
        /// tallest thing on the card.
        /// </summary>
        void BuildListCard()
        {
            listCard = new Card("");
            listCard.SetBounds(Pad, ListY, ContentW, PageH - ListY - 12);
            pagePlayer.Controls.Add(listCard);

            // Snapped to whole rows: a list a fraction of a row too tall shows a
            // sliced track along its bottom edge, which reads as a clipping bug
            // rather than as more to scroll.
            int rows = (listCard.Height - Card.HeaderH - 12) / TrackList.RowH;
            list = new TrackList();
            list.SetBounds(14, Card.HeaderH, ContentW - 28, rows * TrackList.RowH);
            list.Source = playlist;
            list.Activated += delegate(object s, int index) { PlayAt(index); };
            list.SelectionChanged += delegate { UpdateListChrome(); };
            list.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) ShowRowMenu();
            };
            listCard.Controls.Add(list);

            btnAdd = AddListButton("list.add", Ico.File, delegate { AddFilesDialog(); });
            // The one filled button in the row: putting music into an empty
            // player is the action the page is really about.
            btnAdd.Primary = true;
            btnFolder = AddListButton("list.folder", Ico.Folder, delegate { AddFolderDialog(); });
            btnLoad = AddListButton("list.load", Ico.Load, delegate { LoadPlaylistDialog(); });
            btnSave = AddListButton("list.save", Ico.Save, delegate { SavePlaylistDialog(); });
            btnRemove = AddListButton("list.remove", Ico.Minus, delegate { RemoveSelected(); });
            btnClear = AddListButton("list.clear", Ico.Cross, delegate { ClearPlaylist(); });
        }

        Btn AddListButton(string key, IconDraw icon, EventHandler onClick)
        {
            var b = new Btn(Lang.T(key));
            b.Name = key;
            b.Icon = icon;
            b.Height = 26;
            b.Top = 5;
            b.Font = Theme.UiBold(8.5f);
            b.Click += onClick;
            listCard.Controls.Add(b);
            return b;
        }

        /// <summary>Lays the playlist actions out from the right edge of the card
        /// header, each at its own width so a longer translation cannot clip.</summary>
        void LayoutListButtons()
        {
            var row = new[] { btnClear, btnRemove, btnSave, btnLoad, btnFolder, btnAdd };
            int x = listCard.Width - 16;
            foreach (Btn b in row)
            {
                b.FitWidth(74);
                x -= b.Width;
                b.Left = x;
                x -= 6;
            }
        }

        // The play key is the one control on the page the eye should land on
        // first, so it is both larger and the only filled one.
        const int KeySize = 42, PrimarySize = 50;

        KeyBtn AddKey(ref int x, IconDraw icon, bool primary, EventHandler onClick)
        {
            var key = new KeyBtn(icon);
            int size = primary ? PrimarySize : KeySize;
            int centre = Card.HeaderH + (RowH - Card.HeaderH - 12) / 2;
            key.Primary = primary;
            key.SetBounds(x, centre - size / 2, size, size);
            key.Click += onClick;
            transportCard.Controls.Add(key);
            x += size + 6;
            return key;
        }

        void PaintNowPlaying(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Track t = playlist.Current;
            int w = nowCard.Width;

            // A tile rather than cover art: reading the art out of a tag means a
            // decoder and a cache, and this says "a track is loaded" at the same
            // glance for nothing.
            var tile = new RectangleF(20, 40, 68, 68);
            Theme.Fill(g, tile, Theme.RadiusSmall + 2, t != null ? Theme.AccentSoft : Theme.Sunken);
            Theme.Outline(g, tile, Theme.RadiusSmall + 2, Theme.CardLine);
            Ico.Wave(g, new RectangleF(tile.X + 13, tile.Y + 22, 42, 24),
                t != null ? Theme.AccentHot : Theme.Disabled);

            using (var big = Theme.UiBold(14.5f))
            using (var mid = Theme.Ui(9.5f))
            using (var clock = Theme.DigitsBold(18f))
            using (var small = Theme.Digits(10f))
            {
                double length = CurrentDuration();
                double at = position.Dragging ? position.Value : engine.Position;
                string shown = showRemaining && length > 0
                    ? "-" + Util.Time(Math.Max(0, length - at)) : Util.Time(at);
                string total = length > 0 ? Util.Time(length) : "--:--";

                int cw = Theme.Measure(shown, clock);
                int tw = Theme.Measure(total, small);
                int clockLeft = w - 26 - Math.Max(cw, tw);

                const int textX = 104;
                int textW = Math.Max(40, clockLeft - textX - 20);

                string title = t != null ? t.Label : Lang.T("now.nothing");
                Theme.Draw(g, title, big, new Rectangle(textX, 44, textW, 26),
                    t != null ? Theme.Text : Theme.Muted);

                string under;
                if (t != null)
                {
                    var parts = new List<string>();
                    if (t.Album.Length > 0) parts.Add(t.Album);
                    if (t.Year.Length > 0) parts.Add(t.Year);
                    if (playlist.Count > 0)
                        parts.Add(Lang.T("now.position",
                            (playlist.CurrentIndex + 1).ToString(CultureInfo.InvariantCulture),
                            playlist.Count.ToString(CultureInfo.InvariantCulture)));
                    under = string.Join("   ·   ", parts.ToArray());
                }
                else under = Lang.T("now.hint");
                Theme.Draw(g, under, mid, new Rectangle(textX, 76, textW, 20), Theme.Muted);

                // Right-aligned and set in the monospace face, so the layout does
                // not shuffle sideways as the digits change.
                Theme.DrawRight(g, shown, clock, new Rectangle(w - 26 - cw, 42, cw + 4, 28),
                    t != null ? Theme.Text : Theme.Muted);
                Theme.DrawRight(g, total, small, new Rectangle(w - 26 - tw, 76, tw + 4, 20),
                    Theme.Muted);
            }
        }

        void PaintLevels(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = levelsCard.Width;
            using (var cap = Theme.UiBold(8f))
            using (var val = Theme.UiBold(9f))
            {
                Theme.DrawLabel(g, Lang.T("lvl.volume").ToUpperInvariant(), cap,
                    new Point(18, 42), Theme.Muted);
                string vol = Math.Round(volume.Value * 100)
                    .ToString(CultureInfo.InvariantCulture) + "%";
                Theme.DrawRight(g, vol, val, new Rectangle(w - 100, 40, 82, 16), Theme.Text);

                Theme.DrawLabel(g, Lang.T("lvl.balance").ToUpperInvariant(), cap,
                    new Point(18, 80), Theme.Muted);
                double b = balance.Value;
                string bal = Math.Abs(b) < 0.01 ? Lang.T("lvl.centre")
                    : (b < 0 ? "L" : "R") + " "
                      + Math.Round(Math.Abs(b) * 100).ToString(CultureInfo.InvariantCulture);
                Theme.DrawRight(g, bal, val, new Rectangle(w - 100, 78, 82, 16), Theme.Text);
            }
        }

        // ---- Equaliser page ------------------------------------------------------------------

        void BuildEqPage()
        {
            pageEq = NewPage();

            eqCard = new Card("");
            eqCard.SetBounds(Pad, 12, ContentW, 350);
            pageEq.Controls.Add(eqCard);

            btnEqOn = new Btn("");
            btnEqOn.SetBounds(18, Card.HeaderH + 10, 96, 32);
            btnEqOn.Latch = true;
            btnEqOn.Click += delegate
            {
                engine.Eq.Enabled = !engine.Eq.Enabled;
                RefreshEq();
                SaveSettings();
            };
            eqCard.Controls.Add(btnEqOn);

            btnEqPreset = new Btn("");
            btnEqPreset.SetBounds(122, Card.HeaderH + 10, 120, 32);
            btnEqPreset.Click += delegate { ShowPresetMenu(); };
            eqCard.Controls.Add(btnEqPreset);

            btnEqReset = new Btn("");
            btnEqReset.SetBounds(250, Card.HeaderH + 10, 110, 32);
            btnEqReset.Click += delegate
            {
                engine.Eq.SetPreamp(0);
                for (int i = 0; i < Equalizer.Bands.Length; i++) engine.Eq.SetBand(i, 0);
                RefreshEq();
                SaveSettings();
            };
            eqCard.Controls.Add(btnEqReset);

            const int faderY = 100, faderH = 224, faderW = 58;
            preampFader = new Fader();
            preampFader.Label = "PRE";
            preampFader.SetBounds(24, faderY, faderW, faderH);
            preampFader.ValueChanged += delegate
            {
                if (restoring) return;
                engine.Eq.SetPreamp(preampFader.Value);
                curveCard.Invalidate();
                SaveSettings();
            };
            eqCard.Controls.Add(preampFader);

            int bandsX = 136;
            int step = (ContentW - 24 - bandsX) / Equalizer.Bands.Length;
            for (int i = 0; i < Equalizer.Bands.Length; i++)
            {
                int index = i;   // captured per iteration, not shared by all handlers
                var f = new Fader();
                f.Label = BandLabel(Equalizer.Bands[i]);
                f.SetBounds(bandsX + i * step + (step - faderW) / 2, faderY, faderW, faderH);
                f.ValueChanged += delegate
                {
                    if (restoring) return;
                    engine.Eq.SetBand(index, bandFaders[index].Value);
                    curveCard.Invalidate();
                    SaveSettings();
                };
                bandFaders[i] = f;
                eqCard.Controls.Add(f);
            }

            curveCard = new Card("");
            curveCard.SetBounds(Pad, 374, ContentW, PageH - 374 - 12);
            curveCard.Paint += PaintCurve;
            pageEq.Controls.Add(curveCard);
        }

        static string BandLabel(double hz)
        {
            if (hz >= 1000) return (hz / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return hz.ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>The response the faders add up to: a smooth line with the area
        /// under it washed in the accent, which is how a plot reads at a glance
        /// when nobody is going to stop and follow the curve.</summary>
        void PaintCurve(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            var box = new Rectangle(18, Card.HeaderH + 6,
                curveCard.Width - 36, curveCard.Height - Card.HeaderH - 24);
            if (box.Width <= 8 || box.Height <= 8) return;

            Theme.Fill(g, box, Theme.RadiusSmall, Theme.Sunken);
            int mid = box.Y + box.Height / 2;
            using (var pen = new Pen(Theme.CardLine))
            {
                pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                for (int i = 1; i < 4; i++)
                    g.DrawLine(pen, box.X + 8, box.Y + box.Height * i / 4,
                        box.Right - 8, box.Y + box.Height * i / 4);
            }

            int n = Equalizer.Bands.Length;
            var curve = new double[n];
            for (int i = 0; i < n; i++)
                curve[i] = -engine.Eq.GetBand(i) / Equalizer.MaxGainDb * (box.Height / 2 - 6);

            var pts = new PointF[box.Width];
            for (int x = 0; x < box.Width; x++)
            {
                double t = x * (n - 1) / (double)(box.Width - 1);
                int i = (int)t;
                if (i > n - 2) i = n - 2;
                double f = t - i;
                // A straight run between the points would be a zigzag; a
                // Catmull-Rom spline gives the smooth shape a real plot has.
                double v = Spline(curve[Math.Max(0, i - 1)], curve[i],
                    curve[i + 1], curve[Math.Min(n - 1, i + 2)], f);
                float py = mid + (float)v;
                if (py < box.Y + 2) py = box.Y + 2;
                if (py > box.Bottom - 2) py = box.Bottom - 2;
                pts[x] = new PointF(box.X + x, py);
            }

            bool on = engine.Eq.Enabled;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Region clip = g.Clip;
            g.SetClip(box);
            using (var area = new System.Drawing.Drawing2D.GraphicsPath())
            {
                area.AddLines(pts);
                area.AddLine(pts[pts.Length - 1].X, mid, pts[0].X, mid);
                area.CloseFigure();
                using (var b = new SolidBrush(on ? Color.FromArgb(46, Theme.Accent)
                                                 : Color.FromArgb(24, Theme.Muted)))
                    g.FillPath(b, area);
            }
            using (var pen = new Pen(on ? Theme.AccentHot : Theme.Disabled, 2f))
                g.DrawLines(pen, pts);
            g.Clip = clip;
        }

        static double Spline(double a, double b, double c, double d, double t)
        {
            return 0.5 * ((2 * b) + (-a + c) * t
                + (2 * a - 5 * b + 4 * c - d) * t * t
                + (-a + 3 * b - 3 * c + d) * t * t * t);
        }

        void ShowPresetMenu()
        {
            ContextMenuStrip menu = NewMenu();
            for (int i = 0; i < Equalizer.PresetNames.Length; i++)
            {
                int index = i;   // captured per iteration
                var item = new ToolStripMenuItem(Equalizer.PresetNames[i]);
                item.Click += delegate
                {
                    double[] curve = Equalizer.Preset(index);
                    for (int b = 0; b < curve.Length; b++) engine.Eq.SetBand(b, curve[b]);
                    RefreshEq();
                    SaveSettings();
                };
                menu.Items.Add(item);
            }
            menu.Show(btnEqPreset, new Point(0, btnEqPreset.Height));
        }

        /// <summary>Pushes the engine's curve back onto the faders. The guard stops
        /// each fader's own handler writing it straight back out.</summary>
        void RefreshEq()
        {
            restoring = true;
            try
            {
                if (preampFader != null) preampFader.SetSilent(engine.Eq.PreampDb);
                for (int i = 0; i < bandFaders.Length; i++)
                    if (bandFaders[i] != null) bandFaders[i].SetSilent(engine.Eq.GetBand(i));
            }
            finally { restoring = false; }

            if (btnEqOn != null)
            {
                btnEqOn.On = engine.Eq.Enabled;
                btnEqOn.Text = Lang.T(engine.Eq.Enabled ? "eq.on" : "eq.off");
                btnEqOn.Invalidate();
            }
            if (curveCard != null) curveCard.Invalidate();
        }

        // ---- Settings page -----------------------------------------------------------------

        void BuildSettingsPage()
        {
            pageSetup = NewPage();

            schemeCard = new Card("");
            schemeCard.SetBounds(Pad, 12, ContentW, 116);
            pageSetup.Controls.Add(schemeCard);

            int x = 18;
            foreach (Palette p in Palette.All)
            {
                Palette scheme = p;   // captured per iteration
                var b = new SchemeBtn(p);
                b.SetBounds(x, Card.HeaderH + 16, 128, 36);
                b.Click += delegate
                {
                    Theme.Use(scheme.Id);
                    RefreshSchemeButtons();
                    SaveSettings();
                };
                schemeCard.Controls.Add(b);
                schemeButtons.Add(b);
                x += 136;
            }

            langCard = new Card("");
            langCard.SetBounds(Pad, 140, ContentW, 108);
            pageSetup.Controls.Add(langCard);

            btnEn = new Btn("English");
            btnEn.SetBounds(18, Card.HeaderH + 14, 160, 36);
            btnEn.Latch = true;
            btnEn.Click += delegate { SetLanguage(Lang.English); };
            langCard.Controls.Add(btnEn);

            btnUk = new Btn("Українська");
            btnUk.SetBounds(186, Card.HeaderH + 14, 160, 36);
            btnUk.Latch = true;
            btnUk.Click += delegate { SetLanguage(Lang.Ukrainian); };
            langCard.Controls.Add(btnUk);

            updatesCard = new Card("");
            updatesCard.SetBounds(Pad, 260, ContentW, 130);
            updatesCard.Paint += PaintUpdates;
            pageSetup.Controls.Add(updatesCard);

            btnAutoUpdate = new Btn("");
            btnAutoUpdate.SetBounds(18, Card.HeaderH + 12, 300, 34);
            btnAutoUpdate.Latch = true;
            btnAutoUpdate.Click += delegate
            {
                autoUpdate = !autoUpdate;
                btnAutoUpdate.On = autoUpdate;
                btnAutoUpdate.Invalidate();
                SaveSettings();
                RefreshUpdateNote();
                // Switching it back on should act on that straight away rather
                // than waiting for whenever the daily window next opens.
                if (autoUpdate) MaybeCheckAppUpdate();
            };
            updatesCard.Controls.Add(btnAutoUpdate);

            btnCheckUpdate = new Btn("");
            btnCheckUpdate.SetBounds(326, Card.HeaderH + 12, 210, 34);
            btnCheckUpdate.Click += delegate { CheckForUpdatesNow(); };
            updatesCard.Controls.Add(btnCheckUpdate);

            btnInstall = new Btn("");
            btnInstall.SetBounds(544, Card.HeaderH + 12, 290, 34);
            btnInstall.Click += delegate { InstallOrUninstall(); };
            updatesCard.Controls.Add(btnInstall);

            aboutCard = new Card("");
            aboutCard.SetBounds(Pad, 402, ContentW, 170);
            aboutCard.Paint += PaintAbout;
            pageSetup.Controls.Add(aboutCard);
        }

        /// <summary>The one line of text in the UPDATES card: what the last check
        /// found, or what the current one is doing. Written by SetUpdateNote.</summary>
        void PaintUpdates(object sender, PaintEventArgs e)
        {
            using (var f = Theme.Ui(9.5f))
                Theme.Draw(e.Graphics, updateNote, f,
                    new Rectangle(18, Card.HeaderH + 56, ContentW - 44, 22), Theme.Muted);
        }

        void RefreshSchemeButtons()
        {
            for (int i = 0; i < schemeButtons.Count; i++)
            {
                schemeButtons[i].On = Palette.All[i].Id == Theme.Current.Id;
                schemeButtons[i].Invalidate();
            }
            if (btnEn != null)
            {
                btnEn.On = Lang.Current == Lang.English;
                btnUk.On = Lang.Current == Lang.Ukrainian;
                btnEn.Invalidate();
                btnUk.Invalidate();
            }
            if (btnAutoUpdate != null)
            {
                btnAutoUpdate.On = autoUpdate;
                btnAutoUpdate.Invalidate();
            }
        }

        void PaintAbout(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var f = Theme.Ui(9.5f))
            using (var bold = Theme.UiBold(11.5f))
            {
                int y = Card.HeaderH + 14;
                Theme.DrawLabel(g, Brand.Product + "  " + Brand.Version, bold,
                    new Point(18, y), Theme.Text);
                Theme.Draw(g, Lang.T("about.line1"), f,
                    new Rectangle(18, y + 30, 780, 20), Theme.Muted);
                Theme.Draw(g, Lang.T("about.line2"), f,
                    new Rectangle(18, y + 52, 780, 20), Theme.Muted);
                Theme.Draw(g, Lang.T("about.decoder"), f,
                    new Rectangle(18, y + 84, 780, 20), Theme.Disabled);
            }
        }

        // ---- Menus ---------------------------------------------------------------------------

        ContextMenuStrip NewMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColours());
            menu.BackColor = Theme.Card;
            menu.ForeColor = Theme.Text;
            menu.ShowImageMargin = false;
            menu.Font = Theme.Ui(9f);
            return menu;
        }

        void ShowRowMenu()
        {
            if (list.SelectionCount == 0) return;
            ContextMenuStrip menu = NewMenu();
            var reveal = new ToolStripMenuItem(Lang.T("list.reveal"));
            reveal.Click += delegate
            {
                foreach (int i in list.Selection)
                {
                    Track t = playlist.At(i);
                    if (t != null) { Util.ShowInFolder(t.Path); break; }
                }
            };
            var remove = new ToolStripMenuItem(Lang.T("list.remove"));
            remove.Click += delegate { RemoveSelected(); };
            menu.Items.Add(reveal);
            menu.Items.Add(remove);
            menu.Show(Cursor.Position);
        }

        // ---- Language --------------------------------------------------------------------------

        void ApplyLanguage()
        {
            foreach (NavTab tab in tabs) tab.Text = Lang.T(tab.Name);
            LayoutTabs();

            nowCard.Header = Lang.T("card.now");
            transportCard.Header = Lang.T("card.transport");
            analyserCard.Header = Lang.T("card.output");
            levelsCard.Header = Lang.T("card.levels");
            listCard.Header = Lang.T("card.playlist");
            eqCard.Header = Lang.T("card.bands");
            curveCard.Header = Lang.T("card.curve");
            schemeCard.Header = Lang.T("card.scheme");
            langCard.Header = Lang.T("card.language");
            updatesCard.Header = Lang.T("card.updates");
            aboutCard.Header = Lang.T("card.about");

            btnAutoUpdate.Text = Lang.T("set.autoUpdate");
            btnCheckUpdate.Text = Lang.T("btn.checkUpdate");
            btnInstall.Text = IsInstalled ? Lang.T("btn.uninstallApp") : Lang.T("btn.installApp");
            RefreshUpdateNote();

            foreach (Control c in listCard.Controls)
            {
                var b = c as Btn;
                if (b != null && !string.IsNullOrEmpty(b.Name)) b.Text = Lang.T(b.Name);
            }
            LayoutListButtons();

            btnEqPreset.Text = Lang.T("eq.preset");
            btnEqPreset.FitWidth(120);
            btnEqReset.Text = Lang.T("eq.reset");
            btnEqReset.FitWidth(110);
            btnEqReset.Left = btnEqPreset.Left + btnEqPreset.Width + 8;

            RefreshEq();
            RefreshSchemeButtons();
            UpdateListChrome();
            UpdateStatus();
            Invalidate(true);
        }

        void SetLanguage(string code)
        {
            if (Lang.Current == code) return;
            Lang.Current = code;
            ApplyLanguage();
            SaveSettings();
        }

        // ---- Readouts ----------------------------------------------------------------------------

        void UpdateListChrome()
        {
            if (listCard == null) return;
            listCard.Note = playlist.Count == 0 ? Lang.T("list.none")
                : Lang.T("list.count", playlist.Count.ToString(CultureInfo.InvariantCulture),
                    Util.TotalTime(playlist.TotalDuration));
            listCard.Invalidate();
            if (btnRemove != null) btnRemove.Enabled = list.SelectionCount > 0;
            if (btnClear != null) btnClear.Enabled = playlist.Count > 0;
            if (btnSave != null) btnSave.Enabled = playlist.Count > 0;
        }

        void UpdateStatus()
        {
            Track t = playlist.Current;
            if (t == null) statusLine = Lang.T("status.idle");
            else
            {
                string state = engine.State == PlayState.Playing ? Lang.T("status.playing")
                             : engine.State == PlayState.Paused ? Lang.T("status.paused")
                             : Lang.T("status.stopped");
                statusLine = state + "   ·   " + t.Path;
            }
            if (status != null) status.Invalidate();
        }

        void UpdateStats()
        {
            if (stats == null) return;
            Track t = playlist.Current;
            stats.Captions = new[]
            {
                Lang.T("stat.format"), Lang.T("stat.bitrate"), Lang.T("stat.rate"),
                Lang.T("stat.channels"), Lang.T("stat.track"), Lang.T("stat.total")
            };
            string format = "—", bitrate = "—", rate = "—", channels = "—";
            if (t != null)
            {
                try
                {
                    string ext = System.IO.Path.GetExtension(t.Path);
                    if (!string.IsNullOrEmpty(ext)) format = ext.TrimStart('.').ToUpperInvariant();
                }
                catch (ArgumentException) { }
                if (t.Bitrate > 0)
                    bitrate = t.Bitrate.ToString(CultureInfo.InvariantCulture) + " kbps";
                if (t.SampleRate > 0) rate = Util.Khz(t.SampleRate) + "Hz";
                if (engine.SourceChannels > 0)
                    channels = Lang.T(engine.SourceChannels == 1 ? "stat.mono" : "stat.stereo");
            }
            stats.Values = new[]
            {
                format, bitrate, rate, channels,
                playlist.CurrentIndex >= 0
                    ? (playlist.CurrentIndex + 1).ToString(CultureInfo.InvariantCulture) : "—",
                playlist.Count.ToString(CultureInfo.InvariantCulture)
            };
            stats.Invalidate();
        }

        // ---- The frame ------------------------------------------------------------------------------

        void Tick()
        {
            // Before the page guard: the daily update check is not the player
            // page's business, and the settings page must not stop the clock.
            MaybeCheckAppUpdate();
            if (activePage != 0) return;
            bool playing = engine.State == PlayState.Playing;

            if (!position.Dragging)
            {
                double length = CurrentDuration();
                position.Max = length > 0 ? length : 1;
                position.SetSilent(engine.Position);
            }
            nowCard.Invalidate();
            if (analyser.Feed(engine, playing)) analyser.Invalidate();
        }

        double CurrentDuration()
        {
            Track t = playlist.Current;
            if (t != null && t.Duration > 0) return t.Duration;
            return engine.Duration;
        }

        // ---- Control handlers -------------------------------------------------------------------------

        void OnSeekBarMoved()
        {
            if (restoring) return;
            if (engine.State == PlayState.Stopped) return;
            if (!position.Dragging) engine.Seek(position.Value);
            nowCard.Invalidate();
        }

        void OnVolumeMoved()
        {
            engine.Volume = Audio.VolumeCurve((float)volume.Value);
            levelsCard.Invalidate();
            if (!restoring) SaveSettings();
        }

        void OnBalanceMoved()
        {
            engine.Balance = (float)balance.Value;
            levelsCard.Invalidate();
            if (!restoring) SaveSettings();
        }
    }

    /// <summary>
    /// A scheme button: the ordinary latching button with a disc of the colour it
    /// selects painted on it. A row of six identically-coloured buttons cannot say
    /// which is which, and translating the names would be worse — a scheme is
    /// chosen by looking at it.
    /// </summary>
    class SchemeBtn : Btn
    {
        readonly Palette scheme;

        public SchemeBtn(Palette p) : base(p.Caption)
        {
            scheme = p;
            Latch = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            var dot = new RectangleF(Width - 28, Height / 2f - 7, 14, 14);
            using (var b = new SolidBrush(scheme.Accent)) g.FillEllipse(b, dot);
            // The chosen scheme fills its own button with its own hue, so the dot
            // on it is the accent on the accent: a thick ring is what keeps it
            // visible there, and a hairline is enough on all the others.
            using (var pen = new Pen(On ? Theme.OnAccent : Theme.CardLine, On ? 2f : 1f))
                g.DrawEllipse(pen, dot);
        }
    }

    /// <summary>The system menu colours are a white slab against this theme; these
    /// are the app's own.</summary>
    class DarkMenuColours : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color MenuItemSelected { get { return Theme.Subtle; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Subtle; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Subtle; } }
        public override Color MenuItemBorder { get { return Theme.Accent; } }
        public override Color MenuBorder { get { return Theme.CardLine; } }
        public override Color SeparatorDark { get { return Theme.CardLine; } }
        public override Color SeparatorLight { get { return Theme.Card; } }
    }
}
