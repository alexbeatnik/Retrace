// Small shared helpers: time formatting the display depends on, the settings
// file's key/value parsing, command-line handling and the few shell calls the
// player makes. Everything here is pure except the last group, so tests can
// reach it without a window.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Retrace
{
    static class Util
    {
        /// <summary>Extensions Windows can decode through Media Foundation. The
        /// last two need the Web Media Extensions from the Store.</summary>
        public static readonly string[] AudioExtensions =
        {
            ".mp3", ".wav", ".flac", ".m4a", ".m4b", ".aac", ".wma",
            ".mp4", ".mkv", ".ogg", ".oga", ".opus", ".weba", ".webm"
        };

        public static bool IsAudioFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext;
            try { ext = Path.GetExtension(path); }
            catch (ArgumentException) { return false; }   // illegal characters
            if (string.IsNullOrEmpty(ext)) return false;
            ext = ext.ToLowerInvariant();
            foreach (string known in AudioExtensions) if (ext == known) return true;
            return false;
        }

        /// <summary>
        /// m:ss, or h:mm:ss once a track runs past an hour. The display is
        /// monospace and hand-placed, so the short form is not padded to a fixed
        /// width — a leading zero on every three-minute song is a wasted column.
        /// </summary>
        public static string Time(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
            long total = (long)seconds;
            long h = total / 3600, m = (total / 60) % 60, s = total % 60;
            if (h > 0)
                return h.ToString(CultureInfo.InvariantCulture) + ":"
                     + m.ToString("00", CultureInfo.InvariantCulture) + ":"
                     + s.ToString("00", CultureInfo.InvariantCulture);
            return m.ToString(CultureInfo.InvariantCulture) + ":"
                 + s.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>The running time of a whole playlist, as a deck would show it.</summary>
        public static string TotalTime(double seconds)
        {
            if (seconds <= 0) return "0:00";
            long total = (long)seconds;
            long h = total / 3600, m = (total / 60) % 60, s = total % 60;
            if (h > 0)
                return h.ToString(CultureInfo.InvariantCulture) + ":"
                     + m.ToString("00", CultureInfo.InvariantCulture) + ":"
                     + s.ToString("00", CultureInfo.InvariantCulture);
            return m.ToString(CultureInfo.InvariantCulture) + ":"
                 + s.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Sample rate as it is printed on a deck: 44.1k, 48k.</summary>
        public static string Khz(int hz)
        {
            if (hz <= 0) return "";
            double k = hz / 1000.0;
            string s = k.ToString(k == Math.Floor(k) ? "0" : "0.0", CultureInfo.InvariantCulture);
            return s + "k";
        }

        // ---- settings.ini ----------------------------------------------------

        /// <summary>
        /// Reads a flat `key=value` file. Unknown keys are kept rather than
        /// dropped, so a file written by a newer build survives a downgrade, and
        /// a malformed line is skipped rather than taking the load down — the
        /// player must always start.
        /// </summary>
        public static Dictionary<string, string> ParseIni(string text)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return map;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return map;
        }

        public static int IniInt(Dictionary<string, string> map, string key, int fallback, int min, int max)
        {
            string s;
            int v;
            if (map == null || !map.TryGetValue(key, out s)) return fallback;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return fallback;
            return v < min ? min : (v > max ? max : v);
        }

        public static double IniDouble(Dictionary<string, string> map, string key,
            double fallback, double min, double max)
        {
            string s;
            double v;
            if (map == null || !map.TryGetValue(key, out s)) return fallback;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return fallback;
            if (double.IsNaN(v) || double.IsInfinity(v)) return fallback;
            return v < min ? min : (v > max ? max : v);
        }

        public static bool IniBool(Dictionary<string, string> map, string key, bool fallback)
        {
            return IniInt(map, key, fallback ? 1 : 0, 0, 1) != 0;
        }

        public static string IniString(Dictionary<string, string> map, string key, string fallback)
        {
            string s;
            if (map == null || !map.TryGetValue(key, out s)) return fallback;
            return s;
        }

        public static string Num(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // ---- M3U -------------------------------------------------------------

        /// <summary>
        /// Pulls the file entries out of an m3u/m3u8. `#EXTINF` and friends are
        /// skipped; a relative entry is resolved against the playlist's own folder,
        /// which is what makes a playlist copied alongside its music still work.
        /// </summary>
        public static List<string> ParseM3u(string text, string baseFolder)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim().TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                // A URL is not something this player can open; skipping it beats
                // handing a nonsense path to the decoder.
                if (line.IndexOf("://", StringComparison.Ordinal) >= 0) continue;
                string full = line;
                try
                {
                    if (!Path.IsPathRooted(full) && !string.IsNullOrEmpty(baseFolder))
                        full = Path.GetFullPath(Path.Combine(baseFolder, full));
                }
                catch (ArgumentException) { continue; }
                catch (NotSupportedException) { continue; }
                catch (PathTooLongException) { continue; }
                list.Add(full);
            }
            return list;
        }

        public static string BuildM3u(IList<string> paths, IList<string> titles, IList<double> durations)
        {
            var sb = new StringBuilder();
            sb.Append("#EXTM3U\r\n");
            for (int i = 0; i < paths.Count; i++)
            {
                long secs = durations != null && i < durations.Count ? (long)durations[i] : -1;
                string title = titles != null && i < titles.Count ? titles[i] : "";
                sb.Append("#EXTINF:").Append(secs.ToString(CultureInfo.InvariantCulture))
                  .Append(',').Append(title).Append("\r\n");
                sb.Append(paths[i]).Append("\r\n");
            }
            return sb.ToString();
        }

        // ---- Text ------------------------------------------------------------

        /// <summary>
        /// Trims a string to fit a fixed number of display cells, ending it with a
        /// marker when it had to be cut. The VFD is monospace, so cells are
        /// characters and no measuring is needed.
        /// </summary>
        public static string Fit(string s, int cells)
        {
            if (s == null) return "";
            if (cells <= 0) return "";
            if (s.Length <= cells) return s;
            if (cells == 1) return "…";
            return s.Substring(0, cells - 1) + "…";
        }

        /// <summary>
        /// A filename with its extension dropped, used as the track title when a
        /// file carries no tags at all. Underscores become spaces because that is
        /// what they stand in for in almost every ripped filename.
        /// </summary>
        public static string TitleFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string name;
            try { name = Path.GetFileNameWithoutExtension(path); }
            catch (ArgumentException) { return path; }
            if (string.IsNullOrEmpty(name)) return path;
            return name.Replace('_', ' ').Trim();
        }

        // ---- Shell -----------------------------------------------------------

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHOpenFolderAndSelectItems(IntPtr pidl, int count, IntPtr items, int flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr ILCreateFromPath(string path);
        [DllImport("shell32.dll")]
        static extern void ILFree(IntPtr pidl);

        /// <summary>
        /// Opens Explorer with the file selected. Done through the shell API
        /// rather than `explorer.exe /select,"..."`, because the command line
        /// mangles paths containing a comma.
        /// </summary>
        public static void ShowInFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            IntPtr pidl = IntPtr.Zero;
            try
            {
                pidl = ILCreateFromPath(path);
                if (pidl != IntPtr.Zero) SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            finally { if (pidl != IntPtr.Zero) ILFree(pidl); }
        }

        /// <summary>
        /// Every audio file under a folder, in the order a listener expects:
        /// folders walked depth-first and alphabetically, files sorted within
        /// each. Unreadable folders are stepped over — one permission-denied
        /// subfolder must not abandon the rest of a library scan.
        /// </summary>
        public static void ScanFolder(string folder, List<string> into, int depth)
        {
            if (depth > 24 || into.Count > 50000) return;   // a reparse-point loop has to end somewhere
            string[] files;
            try
            {
                files = Directory.GetFiles(folder);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            }
            catch (UnauthorizedAccessException) { return; }
            catch (IOException) { return; }

            foreach (string f in files) if (IsAudioFile(f)) into.Add(f);

            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(folder);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            }
            catch (UnauthorizedAccessException) { return; }
            catch (IOException) { return; }

            foreach (string d in dirs) ScanFolder(d, into, depth + 1);
        }

        /// <summary>
        /// Expands a mix of files and folders handed in by a drop or the command
        /// line into a flat list of playable files.
        /// </summary>
        public static List<string> Expand(IEnumerable<string> paths)
        {
            var list = new List<string>();
            if (paths == null) return list;
            foreach (string p in paths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                try
                {
                    if (Directory.Exists(p)) ScanFolder(p, list, 0);
                    else if (File.Exists(p) && IsAudioFile(p)) list.Add(p);
                }
                catch (ArgumentException) { }
                catch (PathTooLongException) { }
            }
            return list;
        }
    }
}
