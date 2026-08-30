// What the keys do, and the background pass that fills in the tags.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Retrace
{
    partial class MainForm
    {
        // Files Windows could not decode, so a broken entry in the middle of a
        // playlist is stepped over once rather than retried on every pass.
        readonly HashSet<string> undecodable =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ---- Transport --------------------------------------------------------

        /// <summary>
        /// The play key. It starts from the beginning rather than resuming — the
        /// original has a separate pause key and this one has always meant "play
        /// this track from the top".
        /// </summary>
        void StartOrRestart()
        {
            if (engine.State == PlayState.Paused) { engine.Resume(); UpdateDisplay(); return; }
            if (playlist.CurrentIndex >= 0) PlayAt(playlist.CurrentIndex);
            else if (playlist.Count > 0) Advance(false);
            else AddFilesDialog();
        }

        /// <summary>Space and the media key: whichever of play and pause makes
        /// sense from where the player currently is.</summary>
        void TogglePlay()
        {
            if (engine.State == PlayState.Playing) engine.Pause();
            else if (engine.State == PlayState.Paused) engine.Resume();
            else { StartOrRestart(); return; }
            UpdateDisplay();
        }

        void PausePlayback()
        {
            if (engine.State == PlayState.Playing) engine.Pause();
            else if (engine.State == PlayState.Paused) engine.Resume();
            UpdateDisplay();
        }

        void StopPlayback()
        {
            engine.Stop();
            UpdateDisplay();
        }

        void PlayPrevious()
        {
            // Within the first few seconds "previous" means the previous track;
            // after that it means the start of this one. Every CD player made
            // since 1985 behaves this way and the muscle memory is universal.
            if (engine.State != PlayState.Stopped && engine.Position > 3)
            {
                engine.Seek(0);
                return;
            }
            int index = playlist.Previous();
            if (index >= 0) PlayAt(index);
        }

        /// <summary>
        /// Moves to the next track. <paramref name="automatic"/> is set when a
        /// track ended by itself, which is what repeat-one keys off and what stops
        /// the player at the end of an un-repeated list.
        /// </summary>
        void Advance(bool automatic)
        {
            if (playlist.Count == 0) return;

            // A run of unplayable files must not recurse or spin: give up after
            // one pass over the whole list.
            for (int attempt = 0; attempt < playlist.Count; attempt++)
            {
                int index = playlist.Next(automatic);
                if (index < 0) { StopPlayback(); return; }
                playlist.CurrentIndex = index;
                if (StartTrack(index)) return;
                // StartTrack failed: ask the playlist for the next one, and drop
                // the automatic flag or repeat-one hands back the same dead file.
                automatic = false;
            }
            StopPlayback();
        }

        void PlayAt(int index)
        {
            if (index < 0 || index >= playlist.Count) return;
            playlist.CurrentIndex = index;
            if (!StartTrack(index)) Advance(false);
        }

        /// <summary>Opens and starts one track. False means Windows had no decoder
        /// for it, which the caller turns into a skip.</summary>
        bool StartTrack(int index)
        {
            Track t = playlist.At(index);
            if (t == null) return false;

            if (!engine.Play(t.Path, 0))
            {
                undecodable.Add(t.Path);
                return false;
            }
            undecodable.Remove(t.Path);

            // The decoder knows the real duration and rate; the tag pass only
            // guessed at them, and for a variable-bitrate mp3 it cannot know.
            if (engine.Duration > 0) t.Duration = engine.Duration;
            t.SampleRate = engine.SampleRate;
            if (engine.Bitrate > 0) t.Bitrate = engine.Bitrate;

            if (list != null) list.EnsureVisible(index);
            UpdateDisplay();
            return true;
        }

        void Nudge(double seconds)
        {
            if (engine.State == PlayState.Stopped) return;
            double target = engine.Position + seconds;
            if (target < 0) target = 0;
            engine.Seek(target);
            if (nowCard != null) nowCard.Invalidate();
        }

        void ToggleShuffle()
        {
            playlist.Shuffle = !playlist.Shuffle;
            UpdateDisplay();
            SaveSettings();
        }

        void CycleRepeat()
        {
            playlist.Repeat = playlist.Repeat == RepeatMode.Off ? RepeatMode.All
                            : playlist.Repeat == RepeatMode.All ? RepeatMode.One
                            : RepeatMode.Off;
            UpdateDisplay();
            SaveSettings();
        }

        // ---- The display ---------------------------------------------------------

        /// <summary>
        /// Pushes the model back onto everything that shows it. Cheap enough to
        /// call on any change: the cards only repaint, and the per-frame work is
        /// in Tick.
        /// </summary>
        void UpdateDisplay()
        {
            if (list != null) list.Reset();
            if (keyShuffle != null)
            {
                keyShuffle.On = playlist.Shuffle;
                keyShuffle.Invalidate();
            }
            if (keyRepeat != null)
            {
                keyRepeat.On = playlist.Repeat != RepeatMode.Off;
                // The glyph carries which of the two repeat modes is engaged; the
                // lit state only says that one of them is.
                keyRepeat.Icon = playlist.Repeat == RepeatMode.One
                    ? new IconDraw(Ico.RepeatOne) : new IconDraw(Ico.RepeatAll);
                keyRepeat.Invalidate();
            }
            if (keyPlay != null)
            {
                keyPlay.Icon = engine.State == PlayState.Playing
                    ? new IconDraw(Ico.Pause) : new IconDraw(Ico.Play);
                keyPlay.Invalidate();
            }
            if (nowCard != null) nowCard.Invalidate();
            UpdateStats();
            UpdateListChrome();
            UpdateStatus();
        }

        // ---- Adding to the playlist ------------------------------------------------

        /// <summary>
        /// Expands and appends paths. <paramref name="startPlaying"/> starts the
        /// first one — what a drop onto a stopped player, or a file opened from
        /// Explorer, is asking for.
        /// </summary>
        void AddAndPlay(List<string> paths, bool startPlaying)
        {
            List<string> files = Util.Expand(paths);
            if (files.Count == 0) return;

            int first = playlist.Add(files);
            // Everything asked for was already in the list. That is not "nothing
            // to do": opening a file the restored session happened to contain
            // still means play it, so fall back to where it already sits.
            if (first < 0) first = playlist.IndexOf(files[0]);

            if (list != null) list.Reset();
            ScanTags();

            if (startPlaying && first >= 0) PlayAt(first);
            else UpdateDisplay();
            SaveSettings();
        }

        void AddFilesDialog()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = Lang.T("dialog.openFiles");
                dialog.Multiselect = true;
                dialog.Filter = Lang.T("dialog.audio") + "|*"
                    + string.Join(";*", Util.AudioExtensions)
                    + "|" + Lang.T("dialog.all") + "|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                AddAndPlay(new List<string>(dialog.FileNames), playlist.Count == 0);
            }
        }

        void AddFolderDialog()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = Lang.T("dialog.openFolder");
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                AddAndPlay(new List<string> { dialog.SelectedPath }, playlist.Count == 0);
            }
        }

        void LoadPlaylistDialog()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = Lang.T("dialog.loadList");
                dialog.Filter = Lang.T("dialog.playlists") + "|*.m3u;*.m3u8|"
                    + Lang.T("dialog.all") + "|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                AddAndPlay(Playlist.ReadM3u(dialog.FileName), playlist.Count == 0);
            }
        }

        void SavePlaylistDialog()
        {
            if (playlist.Count == 0) return;
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = Lang.T("dialog.saveList");
                dialog.Filter = Lang.T("dialog.playlists") + "|*.m3u8";
                dialog.DefaultExt = "m3u8";
                dialog.FileName = "playlist.m3u8";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    // UTF-8 with a BOM: m3u8 is defined as UTF-8, and the BOM is
                    // what stops other players guessing the code page wrong on
                    // paths with non-ASCII characters in them.
                    File.WriteAllText(dialog.FileName, playlist.ToM3u(),
                        new System.Text.UTF8Encoding(true));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        void RemoveSelected()
        {
            if (list == null || list.SelectionCount == 0) return;
            var indices = new List<int>(list.Selection);
            bool removingCurrent = indices.Contains(playlist.CurrentIndex);
            playlist.Remove(indices);
            list.ClearSelection();
            // The playing track went with the selection: there is nothing left to
            // be playing, so stop rather than leave the transport claiming to run.
            if (removingCurrent) engine.Stop();
            UpdateDisplay();
            SaveSettings();
        }

        void ClearPlaylist()
        {
            engine.Stop();
            playlist.Clear();
            undecodable.Clear();
            if (list != null) list.ClearSelection();
            UpdateDisplay();
            SaveSettings();
        }

        // ---- The tag pass -------------------------------------------------------

        // Bumped whenever the playlist changes. A worker captures the value it
        // started with and goes round again if it no longer matches, so entries
        // added while a scan was finishing are still picked up.
        int scanGeneration;
        bool scanRunning;

        /// <summary>
        /// Reads tags for every entry that has not been looked at yet, on a pool
        /// thread. Doing it inline would stall the message loop for the length of
        /// a folder scan, and doing it in the decoder would mean opening every
        /// file twice.
        /// </summary>
        void ScanTags()
        {
            int generation = ++scanGeneration;
            if (scanRunning) return;   // the running pass will pick up the new entries
            scanRunning = true;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    int done = 0;
                    // A copy per pass rather than the live list. The UI thread
                    // clears and rebuilds the playlist underneath this one, and
                    // indexing it from here reads past an end that has just
                    // moved; entries added during a pass are caught by the next.
                    while (true)
                    {
                        bool unscanned = false;
                        Track[] tracks = playlist.Snapshot();
                        for (int i = 0; i < tracks.Length; i++)
                        {
                            Track next = tracks[i];
                            if (next == null || next.Scanned) continue;
                            unscanned = true;

                            TrackTags tags = Tags.Read(next.Path);
                            next.Artist = tags.Artist;
                            if (!string.IsNullOrEmpty(tags.Title)) next.Title = tags.Title;
                            next.Album = tags.Album;
                            next.Year = tags.Year;
                            // The decoder is the last word on the length, but it
                            // only ever opens the track being played: without the
                            // header's own answer every row nobody has played yet
                            // reads --:-- and the playlist total is wrong until it
                            // has. A length already measured by the decoder is not
                            // overwritten.
                            if (tags.Duration > 0 && next.Duration <= 0)
                                next.Duration = tags.Duration;
                            next.Scanned = true;

                            // Repaint in batches: a redraw per file turns a
                            // thousand-track folder into a thousand invalidations.
                            if (++done >= 24)
                            {
                                done = 0;
                                OnUi(delegate { UpdateDisplay(); });
                            }
                        }
                        if (!unscanned) break;
                    }
                }
                finally
                {
                    scanRunning = false;
                    OnUi(delegate
                    {
                        UpdateDisplay();
                        // Entries added while this pass was finishing have not been
                        // looked at; go round once more for them.
                        if (generation != scanGeneration) ScanTags();
                    });
                }
            });
        }
    }
}
