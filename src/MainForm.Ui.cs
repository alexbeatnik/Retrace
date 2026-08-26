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

        public const int WinW = 880, WinH = 700;
        const int HeaderH = 58;
        const int StatusH = 26;
        const int PageH = WinH - HeaderH - StatusH;   // 616
        const int Pad = 16;
        const int ContentW = WinW - Pad * 2;          // 848

        // ---- Chrome ---------------------------------------------------------------

        CrtPanel header, pageHost, status;
        NavTab tabPlayer, tabEq, tabSetup;
        readonly List<NavTab> tabs = new List<NavTab>();
        string statusLine = "";
        int activePage;

        // ---- Player page -----------------------------------------------------------

        CrtPanel pagePlayer;
        Card nowCard, transportCard, analyserCard, levelsCard, listCard;
        StatStrip stats;
        Bar position, volume, balance;
        Analyser analyser;
        KeyBtn keyPrev, keyPlay, keyStop, keyNext, keyOpen, keyShuffle, keyRepeat;
        TrackList list;
        Btn btnAdd, btnFolder, btnLoad, btnSave, btnRemove, btnClear;

        // ---- Equaliser page ---------------------------------------------------------

        CrtPanel pageEq;
        Card eqCard, curveCard;
        Fader preampFader;
        readonly Fader[] bandFaders = new Fader[Equalizer.Bands.Length];
        Btn btnEqOn, btnEqReset, btnEqPreset;

        // ---- Settings page ----------------------------------------------------------

        CrtPanel pageSetup;
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

            pageHost = new CrtPanel();
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
            header = new CrtPanel();
            header.Dock = DockStyle.Top;
            header.Height = HeaderH;
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
                x -= 6;
            }
        }

        void PaintHeader(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ico.Wave(g, new RectangleF(Pad, 18, 30, 22), Theme.Text);
            using (var f = Theme.UiBold(14f))
                Theme.DrawTracked(g, "RETRACE", f, new Point(Pad + 40, 19),
                    Theme.Text, Theme.Track + 1f);
            using (var pen = new Pen(Theme.Line))
                g.DrawLine(pen, 0, HeaderH - 1, WinW, HeaderH - 1);
        }

        void BuildStatus()
        {
            status = new CrtPanel();
            status.Dock = DockStyle.Bottom;
            status.Height = StatusH;
            status.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Theme.Line)) e.Graphics.DrawLine(pen, 0, 0, WinW, 0);
                using (var f = Theme.Ui(9f))
                    Theme.DrawTrackedLeft(e.Graphics, statusLine, f,
                        new Rectangle(Pad, 0, WinW - Pad * 2, StatusH), Theme.Muted, Theme.Track);
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

        CrtPanel NewPage()
        {
            var page = new CrtPanel();
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            pageHost.Controls.Add(page);
            return page;
        }

        // ---- Player page ---------------------------------------------------------------

        const int RowY = 164, RowH = 124;
        const int TransportW = 380, AnalyserW = 240;
        const int ListY = 364;

        void BuildPlayerPage()
        {
            pagePlayer = NewPage();

            nowCard = new Card("");
            nowCard.SetBounds(Pad, 14, ContentW, 140);
            nowCard.Paint += PaintNowPlaying;
            pagePlayer.Controls.Add(nowCard);

            position = new Bar();
            position.Min = 0;
            position.Max = 1;
            position.SetBounds(18, 102, ContentW - 36, 24);
            position.ValueChanged += delegate { OnSeekBarMoved(); };
            nowCard.Controls.Add(position);

            // Transport, analyser and levels share one row.
            transportCard = new Card("");
            transportCard.SetBounds(Pad, RowY, TransportW, RowH);
            pagePlayer.Controls.Add(transportCard);

            int keyY = Card.HeaderH + 10;
            int x = 12;
            keyPrev = AddKey(ref x, keyY, Ico.Previous, delegate { PlayPrevious(); });
            keyPlay = AddKey(ref x, keyY, Ico.Play, delegate { TogglePlay(); });
            keyPlay.Primary = true;
            keyStop = AddKey(ref x, keyY, Ico.Stop, delegate { StopPlayback(); });
            keyNext = AddKey(ref x, keyY, Ico.Next, delegate { Advance(false); });
            keyOpen = AddKey(ref x, keyY, Ico.Eject, delegate { AddFilesDialog(); });

            // Shuffle and repeat are the same square key, latched: at this size a
            // caption beside the glyph would not fit, and the lit state says more
            // than the word would.
            x += 10;
            keyShuffle = AddKey(ref x, keyY, Ico.Shuffle, delegate { ToggleShuffle(); });
            keyShuffle.Latch = true;
            keyRepeat = AddKey(ref x, keyY, Ico.RepeatAll, delegate { CycleRepeat(); });
            keyRepeat.Latch = true;

            int analyserX = Pad + TransportW + 12;
            analyserCard = new Card("");
            analyserCard.SetBounds(analyserX, RowY, AnalyserW, RowH);
            pagePlayer.Controls.Add(analyserCard);

            analyser = new Analyser();
            analyser.SetBounds(10, Card.HeaderH, AnalyserW - 20, RowH - Card.HeaderH - 8);
            analyser.ModeChanged += delegate { SaveSettings(); };
            analyserCard.Controls.Add(analyser);

            int levelsX = analyserX + AnalyserW + 12;
            levelsCard = new Card("");
            levelsCard.SetBounds(levelsX, RowY, Pad + ContentW - levelsX, RowH);
            levelsCard.Paint += PaintLevels;
            pagePlayer.Controls.Add(levelsCard);

            volume = new Bar();
            volume.SetBounds(16, 58, levelsCard.Width - 32, 20);
            volume.ValueChanged += delegate { OnVolumeMoved(); };
            levelsCard.Controls.Add(volume);

            balance = new Bar();
            balance.Min = -1;
            balance.Max = 1;
            balance.FromCentre = true;
            balance.Detent = 0.05;
            balance.SetBounds(16, 96, levelsCard.Width - 32, 20);
            balance.ValueChanged += delegate { OnBalanceMoved(); };
            levelsCard.Controls.Add(balance);

            stats = new StatStrip();
            stats.SetBounds(Pad, 298, ContentW, 56);
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
            listCard.SetBounds(Pad, ListY, ContentW, PageH - ListY - 14);
            pagePlayer.Controls.Add(listCard);

            // Snapped to whole rows: a list a fraction of a row too tall shows a
            // sliced track along its bottom edge, which reads as a clipping bug
            // rather than as more to scroll.
            int rows = (listCard.Height - Card.HeaderH - 14) / TrackList.RowH;
            list = new TrackList();
            list.SetBounds(14, Card.HeaderH + 2, ContentW - 28, rows * TrackList.RowH);
            list.Source = playlist;
            list.Activated += delegate(object s, int index) { PlayAt(index); };
            list.SelectionChanged += delegate { UpdateListChrome(); };
            list.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) ShowRowMenu();
            };
            listCard.Controls.Add(list);

            btnAdd = AddListButton("list.add", Ico.File, delegate { AddFilesDialog(); });
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
            b.Height = 24;
            b.Top = 4;
            b.Click += onClick;
            listCard.Controls.Add(b);
            return b;
        }

        /// <summary>Lays the playlist actions out from the right edge of the card
        /// header, each at its own width so a longer translation cannot clip.</summary>
        void LayoutListButtons()
        {
            var row = new[] { btnClear, btnRemove, btnSave, btnLoad, btnFolder, btnAdd };
            int x = listCard.Width - 14;
            foreach (Btn b in row)
            {
                b.FitWidth(74);
                x -= b.Width;
                b.Left = x;
                x -= 6;
            }
        }

        KeyBtn AddKey(ref int x, int y, IconDraw icon, EventHandler onClick)
        {
            var key = new KeyBtn(icon);
            key.SetBounds(x, y, 46, 46);
            key.Click += onClick;
            transportCard.Controls.Add(key);
            x += 46 + 5;
            return key;
        }

        void PaintNowPlaying(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Track t = playlist.Current;
            int w = nowCard.Width;

            using (var big = Theme.UiBold(15f))
            using (var mid = Theme.Ui(10f))
            using (var clock = Theme.UiBold(15f))
            {
                double length = CurrentDuration();
                double at = position.Dragging ? position.Value : engine.Position;
                string shown = showRemaining && length > 0
                    ? "-" + Util.Time(Math.Max(0, length - at)) : Util.Time(at);
                string total = length > 0 ? Util.Time(length) : "--:--";

                int cw = Theme.Measure(shown, clock);
                int tw = Theme.Measure(total, mid);
                int clockLeft = w - 24 - Math.Max(cw, tw);

                string title = t != null ? t.Label : Lang.T("now.nothing");
                Theme.Draw(g, title, big, new Rectangle(18, 42, clockLeft - 34, 28),
                    t != null ? Theme.Bright : Theme.Muted);

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
                Theme.Draw(g, under, mid, new Rectangle(18, 72, clockLeft - 34, 22), Theme.Muted);

                // Right-aligned and monospace, so the layout does not shuffle as
                // the digits change.
                Theme.Draw(g, shown, clock, new Rectangle(w - 24 - cw, 40, cw + 4, 28), Theme.Text);
                Theme.Draw(g, total, mid, new Rectangle(w - 24 - tw, 74, tw + 4, 20), Theme.Muted);
            }

            using (var f = Theme.UiBold(9f))
                Theme.DrawTracked(g, Lang.T("card.now").ToUpperInvariant(), f, new Point(14, 11),
                    Theme.Bright, Theme.Track);
        }

        void PaintLevels(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = levelsCard.Width;
            using (var cap = Theme.UiBold(8f))
            using (var val = Theme.Ui(9.5f))
            {
                Theme.DrawTracked(g, Lang.T("lvl.volume").ToUpperInvariant(), cap,
                    new Point(16, 44), Theme.Muted, Theme.Track);
                string vol = Math.Round(volume.Value * 100)
                    .ToString(CultureInfo.InvariantCulture) + "%";
                int vw = Theme.Measure(vol, val);
                Theme.Draw(g, vol, val, new Rectangle(w - 18 - vw, 40, vw + 4, 18), Theme.Text);

                Theme.DrawTracked(g, Lang.T("lvl.balance").ToUpperInvariant(), cap,
                    new Point(16, 82), Theme.Muted, Theme.Track);
                double b = balance.Value;
                string bal = Math.Abs(b) < 0.01 ? Lang.T("lvl.centre")
                    : (b < 0 ? "L" : "R") + " "
                      + Math.Round(Math.Abs(b) * 100).ToString(CultureInfo.InvariantCulture);
                int bw = Theme.Measure(bal, val);
                Theme.Draw(g, bal, val, new Rectangle(w - 18 - bw, 78, bw + 4, 18), Theme.Text);
            }
        }

        // ---- Equaliser page ------------------------------------------------------------------

        void BuildEqPage()
        {
            pageEq = NewPage();

            eqCard = new Card("");
            eqCard.SetBounds(Pad, 14, ContentW, 330);
            pageEq.Controls.Add(eqCard);

            btnEqOn = new Btn("");
            btnEqOn.SetBounds(14, Card.HeaderH + 8, 96, 30);
            btnEqOn.Latch = true;
            btnEqOn.Click += delegate
            {
                engine.Eq.Enabled = !engine.Eq.Enabled;
                RefreshEq();
                SaveSettings();
            };
            eqCard.Controls.Add(btnEqOn);

            btnEqPreset = new Btn("");
            btnEqPreset.SetBounds(118, Card.HeaderH + 8, 116, 30);
            btnEqPreset.Click += delegate { ShowPresetMenu(); };
            eqCard.Controls.Add(btnEqPreset);

            btnEqReset = new Btn("");
            btnEqReset.SetBounds(242, Card.HeaderH + 8, 106, 30);
            btnEqReset.Click += delegate
            {
                engine.Eq.SetPreamp(0);
                for (int i = 0; i < Equalizer.Bands.Length; i++) engine.Eq.SetBand(i, 0);
                RefreshEq();
                SaveSettings();
            };
            eqCard.Controls.Add(btnEqReset);

            const int faderY = 96, faderH = 210, faderW = 58;
            preampFader = new Fader();
            preampFader.Label = "PRE";
            preampFader.SetBounds(20, faderY, faderW, faderH);
            preampFader.ValueChanged += delegate
            {
                if (restoring) return;
                engine.Eq.SetPreamp(preampFader.Value);
                curveCard.Invalidate();
                SaveSettings();
            };
            eqCard.Controls.Add(preampFader);

            int bandsX = 130;
            int step = (ContentW - 20 - bandsX) / Equalizer.Bands.Length;
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
            curveCard.SetBounds(Pad, 356, ContentW, PageH - 370);
            curveCard.Paint += PaintCurve;
            pageEq.Controls.Add(curveCard);
        }

        static string BandLabel(double hz)
        {
            if (hz >= 1000) return (hz / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return hz.ToString("0", CultureInfo.InvariantCulture);
        }

        void PaintCurve(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            var box = new Rectangle(16, Card.HeaderH + 6,
                curveCard.Width - 32, curveCard.Height - Card.HeaderH - 20);
            if (box.Width <= 4 || box.Height <= 4) return;

            using (var b = new SolidBrush(Theme.Sunken)) g.FillRectangle(b, box);
            int mid = box.Y + box.Height / 2;
            using (var pen = new Pen(Theme.Line))
            {
                pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                for (int i = 1; i < 4; i++)
                    g.DrawLine(pen, box.X, box.Y + box.Height * i / 4,
                        box.Right - 1, box.Y + box.Height * i / 4);
            }

            int n = Equalizer.Bands.Length;
            var curve = new double[n];
            for (int i = 0; i < n; i++)
                curve[i] = -engine.Eq.GetBand(i) / Equalizer.MaxGainDb * (box.Height / 2 - 4);

            Color ink = engine.Eq.Enabled ? Theme.Text : Theme.Line;
            using (var line = new SolidBrush(ink))
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
                    int py = mid + (int)Math.Round(v);
                    if (py < box.Y) py = box.Y;
                    if (py >= box.Bottom) py = box.Bottom - 1;
                    g.FillRectangle(line, box.X + x, py - 1, 1, 3);
                }
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
            schemeCard.SetBounds(Pad, 14, ContentW, 110);
            pageSetup.Controls.Add(schemeCard);

            int x = 14;
            foreach (Palette p in Palette.All)
            {
                Palette scheme = p;   // captured per iteration
                var b = new Btn(p.Caption);
                b.SetBounds(x, Card.HeaderH + 14, 124, 34);
                b.Latch = true;
                b.Click += delegate
                {
                    Theme.Use(scheme.Id);
                    RefreshSchemeButtons();
                    SaveSettings();
                };
                schemeCard.Controls.Add(b);
                schemeButtons.Add(b);
                x += 132;
            }

            langCard = new Card("");
            langCard.SetBounds(Pad, 136, ContentW, 102);
            pageSetup.Controls.Add(langCard);

            btnEn = new Btn("English");
            btnEn.SetBounds(14, Card.HeaderH + 12, 160, 34);
            btnEn.Latch = true;
            btnEn.Click += delegate { SetLanguage(Lang.English); };
            langCard.Controls.Add(btnEn);

            btnUk = new Btn("Українська");
            btnUk.SetBounds(182, Card.HeaderH + 12, 160, 34);
            btnUk.Latch = true;
            btnUk.Click += delegate { SetLanguage(Lang.Ukrainian); };
            langCard.Controls.Add(btnUk);

            updatesCard = new Card("");
            updatesCard.SetBounds(Pad, 250, ContentW, 124);
            updatesCard.Paint += PaintUpdates;
            pageSetup.Controls.Add(updatesCard);

            btnAutoUpdate = new Btn("");
            btnAutoUpdate.SetBounds(14, Card.HeaderH + 12, 300, 34);
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
            btnCheckUpdate.SetBounds(322, Card.HeaderH + 12, 210, 34);
            btnCheckUpdate.Click += delegate { CheckForUpdatesNow(); };
            updatesCard.Controls.Add(btnCheckUpdate);

            btnInstall = new Btn("");
            btnInstall.SetBounds(540, Card.HeaderH + 12, 280, 34);
            btnInstall.Click += delegate { InstallOrUninstall(); };
            updatesCard.Controls.Add(btnInstall);

            aboutCard = new Card("");
            aboutCard.SetBounds(Pad, 386, ContentW, 160);
            aboutCard.Paint += PaintAbout;
            pageSetup.Controls.Add(aboutCard);
        }

        /// <summary>The one line of text in the UPDATES card: what the last check
        /// found, or what the current one is doing. Written by SetUpdateNote.</summary>
        void PaintUpdates(object sender, PaintEventArgs e)
        {
            using (var f = Theme.Ui(9.5f))
                Theme.Draw(e.Graphics, updateNote, f,
                    new Rectangle(16, Card.HeaderH + 56, ContentW - 40, 22), Theme.Muted);
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
            using (var f = Theme.Ui(10f))
            using (var bold = Theme.UiBold(11f))
            {
                int y = Card.HeaderH + 12;
                Theme.Draw(g, Brand.Product + "  " + Brand.Version, bold,
                    new Rectangle(16, y, 400, 22), Theme.Text);
                Theme.Draw(g, Lang.T("about.line1"), f,
                    new Rectangle(16, y + 30, 760, 20), Theme.Muted);
                Theme.Draw(g, Lang.T("about.line2"), f,
                    new Rectangle(16, y + 52, 760, 20), Theme.Muted);
                Theme.Draw(g, Lang.T("about.decoder"), f,
                    new Rectangle(16, y + 82, 760, 20), Theme.Line);
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

            // The now-playing card draws its own header, so the clock can share
            // the row with it.
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
            btnEqPreset.FitWidth(116);
            btnEqReset.Text = Lang.T("eq.reset");
            btnEqReset.FitWidth(106);
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

    /// <summary>The system menu colours are a white slab against this theme; these
    /// are the terminal's own.</summary>
    class DarkMenuColours : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color MenuItemSelected { get { return Theme.Subtle; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Subtle; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Subtle; } }
        public override Color MenuItemBorder { get { return Theme.Text; } }
        public override Color MenuBorder { get { return Theme.Line; } }
        public override Color SeparatorDark { get { return Theme.Line; } }
        public override Color SeparatorLight { get { return Theme.Card; } }
    }
}
