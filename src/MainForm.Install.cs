// Per-user install and uninstall, the same shape AV and WindowsStalker use. No
// administrator rights anywhere and no MSI: the app copies itself into
// %LocalAppData%\Programs\Retrace, writes per-user shortcuts and a per-user
// Uninstall key, and that is the whole installation. It stays a portable exe —
// installing is a convenience, not a different build.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Retrace
{
    partial class MainForm
    {
        const string UninstallKeyPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Retrace";
        const string InstalledExeName = "Retrace.exe";
        const string ShortcutName = "Retrace.lnk";

        internal static string InstallDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\Retrace");
            }
        }

        /// <summary>True when the running exe is the installed copy rather than a
        /// portable one, which is what the Settings button switches between.</summary>
        internal static bool IsInstalled
        {
            get
            {
                try { return IsUnder(Application.ExecutablePath, InstallDir); }
                catch (ArgumentException) { return false; }
                catch (NotSupportedException) { return false; }
                catch (PathTooLongException) { return false; }
            }
        }

        internal static bool IsUnder(string child, string root)
        {
            string c = Normalize(child), r = Normalize(root);
            if (c == null || r == null) return false;
            if (string.Equals(c, r, StringComparison.OrdinalIgnoreCase)) return true;
            if (r.Length > 3) r += Path.DirectorySeparatorChar;
            return c.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }

        static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
        }

        void InstallOrUninstall()
        {
            if (IsInstalled)
            {
                if (MessageBox.Show(this, Lang.T("uninstall.confirm"), Brand.Product,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                // The answer travels with the request: the second instance would
                // otherwise ask the same question again for the one click.
                LaunchMode("--uninstall --confirmed");
            }
            else LaunchMode("--install");
        }

        // Both modes run as a separate short-lived instance so the copy can
        // replace the running exe and so uninstall can delete the folder this
        // process is sitting in.
        void LaunchMode(string argument)
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, argument);
                psi.UseShellExecute = true;
                Process.Start(psi);
                ExitApp();
            }
            catch (Win32Exception ex)
            {
                MessageBox.Show(this, Lang.T("install.failed") + ex.Message, Brand.Product,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---- --install -----------------------------------------------------------

        internal static void RunInstallMode()
        {
            Lang.Current = Lang.SystemDefault();

            var f = new Form();
            f.Text = Lang.T("install.title");
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.ClientSize = new Size(400, 110);
            f.BackColor = Theme.Bg;
            f.Icon = Brand.AppIcon;
            Theme.DarkTitleBar(f);

            var label = new Label();
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.ForeColor = Theme.Text;
            label.Font = Theme.Ui(10f);
            label.Text = Lang.T("install.installing");
            f.Controls.Add(label);

            f.Shown += delegate
            {
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    string error = null;
                    try { DoInstall(); }
                    catch (IOException ex) { error = ex.Message; }
                    catch (UnauthorizedAccessException ex) { error = ex.Message; }
                    catch (SecurityException ex) { error = ex.Message; }
                    try
                    {
                        f.BeginInvoke((MethodInvoker)delegate
                        {
                            f.Hide();
                            if (error != null)
                                MessageBox.Show(Lang.T("install.failed") + error, Brand.Product,
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                            else
                                try { Process.Start(Path.Combine(InstallDir, InstalledExeName)); }
                                catch (Win32Exception) { }
                            Application.ExitThread();
                        });
                    }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                });
            };
            Application.Run(f);
        }

        static void DoInstall()
        {
            // The instance that launched --install is still shutting down and
            // holds the single-instance mutex — give it a moment, otherwise the
            // installed copy started below would just hand it the files and exit.
            System.Threading.Thread.Sleep(1500);

            string srcDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string dst = InstallDir;
            Directory.CreateDirectory(dst);

            string dstExe = Path.Combine(dst, InstalledExeName);
            if (!string.Equals(Application.ExecutablePath, dstExe, StringComparison.OrdinalIgnoreCase))
                File.Copy(Application.ExecutablePath, dstExe, true);

            // Carry the user's own state across, but never overwrite what is
            // already at the destination — a reinstall must not wipe the settings
            // or the saved session of an older install.
            if (!string.Equals(srcDir, dst, StringComparison.OrdinalIgnoreCase))
                foreach (string name in new string[] { "settings.ini", "session.m3u8" })
                    CarryOverFile(Path.Combine(srcDir, name), Path.Combine(dst, name));

            // Shortcuts are non-essential: when Windows Script Host is disabled by
            // policy CreateShortcut throws, and an install whose files are already
            // in place should not fail over a missing .lnk.
            try
            {
                CreateShortcut(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName), dstExe, dst);
                CreateShortcut(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName), dstExe, dst);
            }
            catch (COMException) { }
            catch (MissingMethodException) { }
            catch (TargetInvocationException) { }

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
            {
                if (k != null)
                {
                    k.SetValue("DisplayName", Brand.Product);
                    k.SetValue("DisplayVersion", Brand.Version);
                    k.SetValue("Publisher", "Oleksii Poliakov");
                    k.SetValue("DisplayIcon", dstExe);
                    k.SetValue("InstallLocation", dst);
                    k.SetValue("UninstallString", "\"" + dstExe + "\" --uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", 150, RegistryValueKind.DWord); // KB
                }
            }
        }

        static void CarryOverFile(string src, string dst)
        {
            try
            {
                if (File.Exists(src) && !File.Exists(dst)) File.Copy(src, dst);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // ---- --uninstall ---------------------------------------------------------

        /// <param name="confirmed">Set when the window that launched this has
        /// already asked — the Settings button confirms before closing the player.
        /// Apps and features runs the bare --uninstall, and is asked here.</param>
        internal static void RunUninstallMode(bool confirmed)
        {
            Lang.Current = Lang.SystemDefault();
            if (!confirmed && MessageBox.Show(Lang.T("uninstall.confirm"), Brand.Product,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            System.Threading.Thread.Sleep(1200); // let the main instance close

            string error = null;
            try
            {
                TryDeleteFile(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName));
                TryDeleteFile(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName));

                try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false); }
                catch (ArgumentException) { }

                // The running exe cannot delete itself; schedule the folder for
                // removal by a detached cmd that waits for this process to exit.
                ScheduleFolderRemoval(InstallDir);
            }
            catch (IOException ex) { error = ex.Message; }
            catch (UnauthorizedAccessException ex) { error = ex.Message; }
            catch (SecurityException ex) { error = ex.Message; }

            MessageBox.Show(error == null ? Lang.T("uninstall.done") : Lang.T("uninstall.error") + error,
                Brand.Product, MessageBoxButtons.OK,
                error == null ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }

        // A detached cmd that waits two seconds and removes the folder. This is
        // the one place a child shell is unavoidable — the exe being deleted is
        // the one running — and it is a plain rmdir with a visible, ordinary
        // command line, not a hidden script host.
        static void ScheduleFolderRemoval(string folder)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe",
                    "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + folder + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch (Win32Exception) { }
        }

        // .lnk via WScript.Shell (COM, so no extra dependency and nothing to ship)
        static void CreateShortcut(string lnkPath, string target, string workDir)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return;
            object shell = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                new object[] { lnkPath });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
    }
}
