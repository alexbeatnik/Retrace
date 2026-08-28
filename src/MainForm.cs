// App lifetime: the entry point, the single-instance handshake that hands a
// double-clicked file to the copy already running, and the window-wide input.
//
// The window is built in MainForm.Ui.cs, playback lives in MainForm.Playback.cs
// and the saved state in MainForm.Settings.cs. All UI state stays on the
// message-loop thread — the audio thread reaches it only through OnUi(...),
// which swallows the window-already-closed race.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Retrace
{
    partial class MainForm : Form
    {
        // ---- Interop ---------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        struct CopyDataStruct
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        const int WM_COPYDATA = 0x004A;
        // Tags our own WM_COPYDATA so a message from anything else is ignored.
        static readonly IntPtr OpenPathsTag = new IntPtr(0x41504C59);   // 'APLY'

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref CopyDataStruct data);
        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        const int SW_RESTORE = 9;

        // ---- Entry point -----------------------------------------------------

        [STAThread]
        static int Main(string[] args)
        {
            // The build's first pass calls this to produce the icon its second
            // pass embeds; there is no UI involved and no window to show.
            if (args.Length >= 2 && args[0] == "--write-icon")
                return Brand.WriteIconFile(args[1]);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Install and uninstall run outside the single-instance mutex on
            // purpose: they are launched by the main window as it closes, and
            // taking the mutex would mean waiting for the process they are
            // replacing. Neither opens the sound card, so two copies is fine.
            foreach (string a in args)
            {
                if (a == "--install") { RunInstallMode(); return 0; }
                if (a == "--uninstall") { RunUninstallMode(); return 0; }
            }

            var files = new List<string>();
            foreach (string a in args) if (!a.StartsWith("-")) files.Add(a);

            bool owned;
            using (var mutex = new Mutex(true, "Retrace.SingleInstance.4f1c", out owned))
            {
                if (!owned)
                {
                    // Another copy has the machine's sound card open. Hand it the
                    // files and bow out — two players fighting over a device is
                    // never what a double-clicked file was asking for.
                    HandOff(Util.Expand(files));
                    return 0;
                }

                Application.Run(new MainForm(files));
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        /// <summary>
        /// Sends the paths to the running instance and brings it forward. A
        /// failure here is silent: the user asked to play a file, and a dialog
        /// explaining an IPC problem helps nobody.
        /// </summary>
        static void HandOff(List<string> paths)
        {
            IntPtr target = FindRunningWindow();
            if (target == IntPtr.Zero) return;

            if (paths.Count > 0)
            {
                string joined = string.Join("\n", paths.ToArray());
                IntPtr buffer = IntPtr.Zero;
                try
                {
                    byte[] bytes = Encoding.Unicode.GetBytes(joined + "\0");
                    buffer = Marshal.AllocHGlobal(bytes.Length);
                    Marshal.Copy(bytes, 0, buffer, bytes.Length);
                    var data = new CopyDataStruct();
                    data.dwData = OpenPathsTag;
                    data.cbData = bytes.Length;
                    data.lpData = buffer;
                    // SendMessage, not PostMessage: the buffer has to outlive the
                    // call, and this process is about to exit.
                    SendMessage(target, WM_COPYDATA, IntPtr.Zero, ref data);
                }
                catch (OutOfMemoryException) { }
                finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
            }

            ShowWindow(target, SW_RESTORE);
            SetForegroundWindow(target);
        }

        static IntPtr FindRunningWindow()
        {
            IntPtr found = IntPtr.Zero;
            uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            var ours = new List<uint>();
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(
                    System.Diagnostics.Process.GetCurrentProcess().ProcessName))
                    if ((uint)p.Id != self) ours.Add((uint)p.Id);
            }
            catch (InvalidOperationException) { return IntPtr.Zero; }
            if (ours.Count == 0) return IntPtr.Zero;

            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (!IsWindowVisible(hWnd)) return true;
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (!ours.Contains(pid)) return true;
                found = hWnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_COPYDATA)
            {
                var data = (CopyDataStruct)Marshal.PtrToStructure(m.LParam, typeof(CopyDataStruct));
                if (data.dwData == OpenPathsTag && data.cbData > 0 && data.lpData != IntPtr.Zero)
                {
                    string joined = Marshal.PtrToStringUni(data.lpData, data.cbData / 2);
                    if (!string.IsNullOrEmpty(joined))
                    {
                        var paths = new List<string>(joined.TrimEnd('\0').Split('\n'));
                        AddAndPlay(paths, true);
                    }
                    m.Result = new IntPtr(1);
                    return;
                }
            }
            base.WndProc(ref m);
        }

        // ---- State -----------------------------------------------------------

        readonly Playlist playlist = new Playlist();
        readonly AudioEngine engine = new AudioEngine();
        readonly List<string> startupFiles;

        // The Forms timer, not the threading one: it fires on the message loop,
        // which is the only thread allowed to repaint.
        System.Windows.Forms.Timer refresh;
        bool showRemaining;
        // Set while saved state is being applied, so a control's own change
        // handler does not write half-restored state straight back out.
        bool restoring;

        public MainForm(List<string> files)
        {
            startupFiles = files ?? new List<string>();

            Lang.Current = Lang.SystemDefault();
            LoadSettings();

            Text = Brand.Product;
            Icon = Brand.AppIcon;
            // A fixed layout of hand-placed cards: nothing here reflows, so the
            // window does not offer to be resized.
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;
            DoubleBuffered = true;

            BuildUi();
            ApplySettingsToUi();
            // The caption text is painted out: the in-window header already
            // carries the wordmark, and Form.Text still names the window for the
            // taskbar, Alt+Tab and screen readers.
            Theme.DarkTitleBar(this, true);

            engine.TrackEnded += delegate { OnUi(delegate { Advance(true); }); };

            refresh = new System.Windows.Forms.Timer();
            refresh.Interval = 40;   // 25 fps, which is what the analyser needs
            refresh.Tick += delegate { Tick(); };
            refresh.Start();

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            FormClosing += OnClosingUp;
            Shown += delegate { OnShownUp(); };
        }

        void OnShownUp()
        {
            RestoreSavedPlaylist();
            if (startupFiles.Count > 0) AddAndPlay(startupFiles, true);
            UpdateDisplay();
            // The launch check. Nothing is playing yet unless a file was handed
            // in on the command line, which is exactly when UpdateBusy says no.
            RefreshUpdateNote();
            MaybeCheckAppUpdate();
        }

        void OnClosingUp(object sender, FormClosingEventArgs e)
        {
            refresh.Stop();
            SaveSettings();
            engine.Dispose();
        }

        /// <summary>
        /// Runs an action on the message-loop thread. Used by the audio thread's
        /// track-ended event; a window closed between the post and the pump is an
        /// ordinary shutdown race, not an error.
        /// </summary>
        void OnUi(MethodInvoker action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        // ---- Drag and drop ---------------------------------------------------

        void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (dropped == null) return;
            AddAndPlay(new List<string>(dropped), playlist.Count == 0);
        }

        // ---- Keyboard --------------------------------------------------------

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // The playlist owns the arrows while it has focus; taking them here
            // would make the list unnavigable.
            bool listHasFocus = list != null && list.Focused;

            switch (e.KeyCode)
            {
                case Keys.Space:
                    TogglePlay();
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Right:
                    if (e.Control) Advance(false); else Nudge(5);
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Left:
                    if (e.Control) PlayPrevious(); else Nudge(-5);
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Up:
                    if (listHasFocus) return;
                    volume.Value = volume.Value + 0.05;
                    OnVolumeMoved();
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Down:
                    if (listHasFocus) return;
                    volume.Value = volume.Value - 0.05;
                    OnVolumeMoved();
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Delete:
                    if (activePage == 1) { RemoveSelected(); e.SuppressKeyPress = true; }
                    break;
                case Keys.O:
                    if (e.Control) { AddFilesDialog(); e.SuppressKeyPress = true; }
                    break;
                case Keys.D1: ShowPage(0); e.SuppressKeyPress = true; break;
                case Keys.D2: ShowPage(1); e.SuppressKeyPress = true; break;
                case Keys.D3: ShowPage(2); e.SuppressKeyPress = true; break;
                case Keys.D4: ShowPage(3); e.SuppressKeyPress = true; break;
                case Keys.T:
                    if (e.Control) { NextScheme(); e.SuppressKeyPress = true; }
                    break;
            }
            base.OnKeyDown(e);
        }

        /// <summary>Steps to the next colour scheme. The settings page has the
        /// full set; this is the shortcut for trying them.</summary>
        void NextScheme()
        {
            int at = 0;
            for (int i = 0; i < Palette.All.Length; i++)
                if (Palette.All[i].Id == Theme.Current.Id) { at = i; break; }
            Theme.Use(Palette.All[(at + 1) % Palette.All.Length].Id);
            RefreshSchemeButtons();
            SaveSettings();
        }
    }
}
