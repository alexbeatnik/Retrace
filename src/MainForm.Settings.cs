// settings.ini: what the player was set to and what it was playing.
//
// The file sits next to the executable when that folder is writable — a portable
// exe on a stick should keep its settings on the stick — and falls back to
// %AppData% when it is not, which is what happens under Program Files.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Retrace
{
    partial class MainForm
    {
        const int MaxSavedTracks = 5000;

        string settingsPath;
        // Read from the file at startup and applied once the window exists.
        List<string> savedPlaylist = new List<string>();
        int savedIndex = -1;

        float pendingVolume = 0.7f;
        float pendingBalance;
        int pendingPage;
        AnalyserMode pendingVis = AnalyserMode.Vu;

        string SettingsPath()
        {
            if (settingsPath != null) return settingsPath;
            try
            {
                string beside = Path.Combine(
                    Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath),
                    "settings.ini");
                string probe = beside + ".tmp";
                // Ask the filesystem rather than guessing from the path: a folder
                // can be read-only for reasons that have nothing to do with where
                // it is.
                File.WriteAllText(probe, "");
                File.Delete(probe);
                settingsPath = beside;
                return settingsPath;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Retrace");
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            settingsPath = Path.Combine(dir, "settings.ini");
            return settingsPath;
        }

        void LoadSettings()
        {
            Dictionary<string, string> map;
            try { map = Util.ParseIni(File.ReadAllText(SettingsPath())); }
            catch (IOException) { map = new Dictionary<string, string>(); }
            catch (UnauthorizedAccessException) { map = new Dictionary<string, string>(); }
            catch (ArgumentException) { map = new Dictionary<string, string>(); }

            pendingVolume = (float)Util.IniDouble(map, "volume", 0.7, 0, 1);
            pendingBalance = (float)Util.IniDouble(map, "balance", 0, -1, 1);
            pendingPage = Util.IniInt(map, "page", 0, 0, 3);
            pendingVis = (AnalyserMode)Util.IniInt(map, "vis", 0, 0, 3);
            // The scheme has to be in place before anything paints, so it is
            // applied here rather than waiting for ApplySettingsToUi.
            Theme.Use(Util.IniString(map, "scheme", "amber"));

            playlist.Shuffle = Util.IniBool(map, "shuffle", false);
            playlist.Repeat = (RepeatMode)Util.IniInt(map, "repeat", 0, 0, 2);
            showRemaining = Util.IniBool(map, "remaining", false);
            Lang.Current = Util.IniString(map, "language", Lang.SystemDefault());

            autoUpdate = Util.IniBool(map, "autoupdate", true);
            // Ticks rather than a formatted date: the stamp is only ever compared
            // with DateTime.Now, and a round-trip through a locale-dependent
            // format is one more way for a settings file to travel badly.
            long ticks;
            if (long.TryParse(Util.IniString(map, "lastupdate", ""),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks)
                && ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks)
                lastAppUpdateCheck = new DateTime(ticks);

            engine.Eq.Enabled = Util.IniBool(map, "eq", false);
            engine.Eq.SetPreamp(Util.IniDouble(map, "eq.pre", 0, -12, 12));
            for (int i = 0; i < Equalizer.Bands.Length; i++)
                engine.Eq.SetBand(i, Util.IniDouble(map,
                    "eq." + i.ToString(CultureInfo.InvariantCulture), 0, -12, 12));

            savedIndex = Util.IniInt(map, "track", -1, -1, MaxSavedTracks);
            savedPlaylist.Clear();
            string listFile = Util.IniString(map, "session", "");
            if (!string.IsNullOrEmpty(listFile))
            {
                try
                {
                    // The playlist is kept as its own m3u8 rather than crammed into
                    // the ini: paths contain '=' and newlines far more often than
                    // an ini parser can survive.
                    if (File.Exists(listFile))
                        savedPlaylist = Util.ParseM3u(File.ReadAllText(listFile),
                            Path.GetDirectoryName(listFile));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (ArgumentException) { }
            }
        }

        /// <summary>Pushes the loaded values into the window once it exists. The
        /// guard stops each setter writing the file straight back out while it is
        /// still being filled in.</summary>
        void ApplySettingsToUi()
        {
            restoring = true;
            try
            {
                volume.SetSilent(pendingVolume);
                balance.SetSilent(pendingBalance);
                engine.Volume = Audio.VolumeCurve(pendingVolume);
                engine.Balance = pendingBalance;
                analyser.Mode = pendingVis;
                RefreshEq();
                RefreshSchemeButtons();
                ShowPage(pendingPage);
            }
            finally { restoring = false; }
        }

        void RestoreSavedPlaylist()
        {
            if (savedPlaylist.Count > 0)
            {
                var live = new List<string>();
                foreach (string p in savedPlaylist)
                {
                    try { if (File.Exists(p)) live.Add(p); }
                    catch (ArgumentException) { }
                }
                if (live.Count > 0)
                {
                    playlist.Add(live);
                    // The track is selected but not started: a player that begins
                    // playing the moment it is opened is one nobody leaves running.
                    if (savedIndex >= 0 && savedIndex < playlist.Count)
                        playlist.CurrentIndex = savedIndex;
                    ScanTags();
                }
            }
        }

        void SaveSettings()
        {
            if (restoring) return;
            var sb = new StringBuilder();
            sb.Append("# Retrace settings\r\n");
            Put(sb, "language", Lang.Current);
            Put(sb, "scheme", Theme.Current.Id);
            Put(sb, "volume", Util.Num(volume != null ? volume.Value : pendingVolume));
            Put(sb, "balance", Util.Num(balance != null ? balance.Value : pendingBalance));
            Put(sb, "page", activePage.ToString(CultureInfo.InvariantCulture));
            Put(sb, "shuffle", playlist.Shuffle ? "1" : "0");
            Put(sb, "repeat", ((int)playlist.Repeat).ToString(CultureInfo.InvariantCulture));
            Put(sb, "remaining", showRemaining ? "1" : "0");
            Put(sb, "autoupdate", autoUpdate ? "1" : "0");
            Put(sb, "lastupdate",
                lastAppUpdateCheck.Ticks.ToString(CultureInfo.InvariantCulture));
            Put(sb, "vis", ((int)(analyser != null ? analyser.Mode : pendingVis))
                .ToString(CultureInfo.InvariantCulture));

            Put(sb, "eq", engine.Eq.Enabled ? "1" : "0");
            Put(sb, "eq.pre", Util.Num(engine.Eq.PreampDb));
            for (int i = 0; i < Equalizer.Bands.Length; i++)
                Put(sb, "eq." + i.ToString(CultureInfo.InvariantCulture),
                    Util.Num(engine.Eq.GetBand(i)));

            string sessionFile = SaveSessionPlaylist();
            if (sessionFile != null) Put(sb, "session", sessionFile);
            Put(sb, "track", playlist.CurrentIndex.ToString(CultureInfo.InvariantCulture));

            WriteAtomic(SettingsPath(), sb.ToString(), new UTF8Encoding(false));
        }

        static void Put(StringBuilder sb, string key, string value)
        {
            sb.Append(key).Append('=').Append(value).Append("\r\n");
        }

        /// <summary>Writes the session playlist beside the settings file. Returns
        /// its path, or null when there was nothing to write.</summary>
        string SaveSessionPlaylist()
        {
            if (playlist.Count == 0) return null;
            string path;
            try { path = Path.Combine(Path.GetDirectoryName(SettingsPath()), "session.m3u8"); }
            catch (ArgumentException) { return null; }

            var paths = new List<string>();
            var titles = new List<string>();
            var durations = new List<double>();
            IList<Track> tracks = playlist.Tracks;
            int count = Math.Min(tracks.Count, MaxSavedTracks);
            for (int i = 0; i < count; i++)
            {
                paths.Add(tracks[i].Path);
                titles.Add(tracks[i].Label);
                durations.Add(tracks[i].Duration);
            }
            return WriteAtomic(path, Util.BuildM3u(paths, titles, durations),
                new UTF8Encoding(true)) ? path : null;
        }

        /// <summary>
        /// Writes through a temporary file and moves it into place, so a crash or
        /// a power cut cannot leave a truncated settings file behind — the old one
        /// survives intact instead.
        /// </summary>
        static bool WriteAtomic(string path, string text, Encoding encoding)
        {
            string tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, text, encoding);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
            return false;
        }
    }
}
