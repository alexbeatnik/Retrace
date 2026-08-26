// The playlist and the rules for moving through it. No UI and no audio: this is
// the part the tests exercise directly, because "which track comes next" is
// where a player's behaviour actually lives.
using System;
using System.Collections.Generic;
using System.IO;

namespace Retrace
{
    enum RepeatMode { Off, All, One }

    sealed class Track
    {
        /// <summary>Absolute path — also the track's identity in the list.</summary>
        public string Path;
        public string Artist = "";
        public string Title = "";
        public string Album = "";
        public string Year = "";
        /// <summary>Seconds; 0 until the metadata scan reaches this entry.</summary>
        public double Duration;
        public int SampleRate;
        public int Bitrate;
        /// <summary>Set once the scanner has been over it, so the display can tell
        /// "no tags" from "not looked at yet".</summary>
        public bool Scanned;

        public Track(string path)
        {
            Path = path;
            Title = Util.TitleFromPath(path);
        }

        /// <summary>The line the display and the playlist show: "artist — title"
        /// where both are known, and the filename where neither is.</summary>
        public string Label
        {
            get
            {
                bool hasArtist = !string.IsNullOrEmpty(Artist);
                bool hasTitle = !string.IsNullOrEmpty(Title);
                if (hasArtist && hasTitle) return Artist + " — " + Title;
                if (hasTitle) return Title;
                if (hasArtist) return Artist;
                return Util.TitleFromPath(Path);
            }
        }
    }

    sealed class Playlist
    {
        readonly List<Track> tracks = new List<Track>();
        // The order shuffle walks in: a permutation of the indices, regenerated
        // whenever the list changes underneath it.
        readonly List<int> shuffleOrder = new List<int>();
        readonly Random random = new Random();
        int current = -1;

        public IList<Track> Tracks { get { return tracks; } }
        public int Count { get { return tracks.Count; } }
        public bool Shuffle { get; set; }
        public RepeatMode Repeat { get; set; }

        public int CurrentIndex
        {
            get { return current; }
            set { current = value >= 0 && value < tracks.Count ? value : -1; }
        }

        public Track Current
        {
            get { return current >= 0 && current < tracks.Count ? tracks[current] : null; }
        }

        public Track At(int index)
        {
            return index >= 0 && index < tracks.Count ? tracks[index] : null;
        }

        public double TotalDuration
        {
            get
            {
                double total = 0;
                for (int i = 0; i < tracks.Count; i++) total += tracks[i].Duration;
                return total;
            }
        }

        /// <summary>
        /// Appends files, skipping ones already in the list. Returns the index of
        /// the first entry actually added, or -1 if every one was a duplicate —
        /// which is what lets a drop start playing what was dropped.
        /// </summary>
        public int Add(IEnumerable<string> paths)
        {
            if (paths == null) return -1;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tracks.Count; i++) seen.Add(tracks[i].Path);

            int first = -1;
            foreach (string p in paths)
            {
                if (string.IsNullOrEmpty(p) || !seen.Add(p)) continue;
                if (first < 0) first = tracks.Count;
                tracks.Add(new Track(p));
            }
            if (first >= 0) InvalidateShuffle();
            return first;
        }

        /// <summary>
        /// Where a path sits in the list, or -1. Opening a file that is already in
        /// the playlist has to find it rather than add a second copy — that is
        /// what makes double-clicking a track in Explorer play it even when the
        /// restored session already contained it.
        /// </summary>
        public int IndexOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return -1;
            for (int i = 0; i < tracks.Count; i++)
                if (string.Equals(tracks[i].Path, path, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        public void Clear()
        {
            tracks.Clear();
            shuffleOrder.Clear();
            current = -1;
        }

        /// <summary>
        /// Removes the given positions. The playing track's index is carried
        /// across the removal rather than recomputed, so deleting entries above it
        /// does not silently switch what is playing.
        /// </summary>
        public void Remove(IEnumerable<int> indices)
        {
            if (indices == null) return;
            var drop = new HashSet<int>();
            foreach (int i in indices) if (i >= 0 && i < tracks.Count) drop.Add(i);
            if (drop.Count == 0) return;

            string playing = current >= 0 && current < tracks.Count ? tracks[current].Path : null;
            bool playingDropped = current >= 0 && drop.Contains(current);

            var kept = new List<Track>(tracks.Count - drop.Count);
            for (int i = 0; i < tracks.Count; i++) if (!drop.Contains(i)) kept.Add(tracks[i]);
            tracks.Clear();
            tracks.AddRange(kept);

            if (playingDropped || playing == null) current = -1;
            else
            {
                current = -1;
                for (int i = 0; i < tracks.Count; i++)
                    if (tracks[i].Path == playing) { current = i; break; }
            }
            InvalidateShuffle();
        }

        /// <summary>
        /// The next track, or -1 when the list has finished. <paramref
        /// name="automatic"/> distinguishes a track ending on its own from the
        /// user pressing the button: repeat-one repeats only for the former, and
        /// only the former stops at the end of an un-repeated list.
        /// </summary>
        public int Next(bool automatic)
        {
            if (tracks.Count == 0) return -1;
            if (current < 0) return FirstInOrder();

            if (automatic && Repeat == RepeatMode.One) return current;

            if (Shuffle)
            {
                EnsureShuffle();
                int at = shuffleOrder.IndexOf(current);
                if (at < 0) return FirstInOrder();
                if (at + 1 < shuffleOrder.Count) return shuffleOrder[at + 1];
                if (Repeat == RepeatMode.All || !automatic)
                {
                    // A fresh permutation at the end of every pass, so a second
                    // time through is not the same running order as the first.
                    Reshuffle();
                    return shuffleOrder.Count > 0 ? shuffleOrder[0] : -1;
                }
                return -1;
            }

            if (current + 1 < tracks.Count) return current + 1;
            if (Repeat == RepeatMode.All || !automatic) return 0;
            return -1;
        }

        /// <summary>The previous track. Pressing back always moves, even under
        /// repeat-one — that is the point of pressing it.</summary>
        public int Previous()
        {
            if (tracks.Count == 0) return -1;
            if (current < 0) return FirstInOrder();

            if (Shuffle)
            {
                EnsureShuffle();
                int at = shuffleOrder.IndexOf(current);
                if (at <= 0) return shuffleOrder.Count > 0
                    ? shuffleOrder[shuffleOrder.Count - 1] : -1;
                return shuffleOrder[at - 1];
            }
            if (current - 1 >= 0) return current - 1;
            return tracks.Count - 1;
        }

        int FirstInOrder()
        {
            if (tracks.Count == 0) return -1;
            if (!Shuffle) return 0;
            EnsureShuffle();
            return shuffleOrder.Count > 0 ? shuffleOrder[0] : 0;
        }

        void InvalidateShuffle() { shuffleOrder.Clear(); }

        void EnsureShuffle()
        {
            if (shuffleOrder.Count != tracks.Count) Reshuffle();
        }

        void Reshuffle()
        {
            shuffleOrder.Clear();
            for (int i = 0; i < tracks.Count; i++) shuffleOrder.Add(i);
            // Fisher-Yates, walked downwards.
            for (int i = shuffleOrder.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int t = shuffleOrder[i];
                shuffleOrder[i] = shuffleOrder[j];
                shuffleOrder[j] = t;
            }
            // The track playing when the reshuffle happens has to lead the new
            // order, or it is visited twice — once now and once in the new pass.
            if (current >= 0)
            {
                int at = shuffleOrder.IndexOf(current);
                if (at > 0)
                {
                    shuffleOrder[at] = shuffleOrder[0];
                    shuffleOrder[0] = current;
                }
            }
        }

        // ---- M3U -------------------------------------------------------------

        public string ToM3u()
        {
            var paths = new List<string>();
            var titles = new List<string>();
            var durations = new List<double>();
            for (int i = 0; i < tracks.Count; i++)
            {
                paths.Add(tracks[i].Path);
                titles.Add(tracks[i].Label);
                durations.Add(tracks[i].Duration);
            }
            return Util.BuildM3u(paths, titles, durations);
        }

        /// <summary>
        /// Loads an m3u/m3u8, keeping only entries that still exist. A playlist
        /// referring to files that have been moved should come up short rather
        /// than fill the list with rows that cannot play.
        /// </summary>
        public static List<string> ReadM3u(string path)
        {
            try
            {
                string text = File.ReadAllText(path);
                string folder = Path.GetDirectoryName(Path.GetFullPath(path));
                var wanted = Util.ParseM3u(text, folder);
                var live = new List<string>();
                foreach (string p in wanted)
                    if (Util.IsAudioFile(p) && File.Exists(p)) live.Add(p);
                return live;
            }
            catch (IOException) { return new List<string>(); }
            catch (UnauthorizedAccessException) { return new List<string>(); }
            catch (ArgumentException) { return new List<string>(); }
        }
    }
}
