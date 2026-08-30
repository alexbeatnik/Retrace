// Which track comes next — where a player's behaviour actually lives.
using System;
using System.Collections.Generic;
using System.Threading;

namespace Retrace.Tests
{
    public static class PlaylistTests
    {
        static Playlist Three()
        {
            var list = new Playlist();
            list.Add(new List<string> { @"C:\a.mp3", @"C:\b.mp3", @"C:\c.mp3" });
            return list;
        }

        public static void TestAddReportsTheFirstNewEntry()
        {
            var list = new Playlist();
            Assert.Equal(0, list.Add(new List<string> { @"C:\a.mp3" }), "first add starts at 0");
            Assert.Equal(1, list.Add(new List<string> { @"C:\b.mp3" }), "second lands after it");
            Assert.Equal(2, list.Count, "both are in");
        }

        public static void TestAddSkipsDuplicates()
        {
            var list = Three();
            Assert.Equal(-1, list.Add(new List<string> { @"C:\b.mp3" }),
                "an entry already present adds nothing");
            Assert.Equal(3, list.Count, "and does not grow the list");
        }

        public static void TestIndexOfIsCaseInsensitive()
        {
            // Windows paths are case-insensitive, and Explorer will hand over a
            // path cased differently from the one the session saved.
            var list = Three();
            Assert.Equal(1, list.IndexOf(@"C:\B.MP3"), "found regardless of case");
            Assert.Equal(-1, list.IndexOf(@"C:\zz.mp3"), "absent");
            Assert.Equal(-1, list.IndexOf(null), "null");
        }

        public static void TestNextWalksForwardAndStops()
        {
            var list = Three();
            list.CurrentIndex = 0;
            Assert.Equal(1, list.Next(true), "0 to 1");
            list.CurrentIndex = 2;
            Assert.Equal(-1, list.Next(true),
                "the end of an un-repeated list stops rather than wrapping");
        }

        public static void TestNextByHandWrapsEvenWithoutRepeat()
        {
            // Pressing the key is a request, not the list running out: it always
            // moves, which is the difference the automatic flag encodes.
            var list = Three();
            list.CurrentIndex = 2;
            Assert.Equal(0, list.Next(false), "the button wraps");
        }

        public static void TestRepeatAllWraps()
        {
            var list = Three();
            list.Repeat = RepeatMode.All;
            list.CurrentIndex = 2;
            Assert.Equal(0, list.Next(true), "repeat-all comes back round");
        }

        public static void TestRepeatOneOnlyRepeatsWhenTheTrackEnded()
        {
            var list = Three();
            list.Repeat = RepeatMode.One;
            list.CurrentIndex = 1;
            Assert.Equal(1, list.Next(true), "a track that ended plays again");
            Assert.Equal(2, list.Next(false), "but the next key still moves on");
        }

        public static void TestPreviousWraps()
        {
            var list = Three();
            list.CurrentIndex = 0;
            Assert.Equal(2, list.Previous(), "back from the first goes to the last");
            list.CurrentIndex = 2;
            Assert.Equal(1, list.Previous(), "and otherwise steps back one");
        }

        public static void TestEmptyListHasNowhereToGo()
        {
            var list = new Playlist();
            Assert.Equal(-1, list.Next(true), "next");
            Assert.Equal(-1, list.Previous(), "previous");
            Assert.Equal(null, list.Current, "no current track");
        }

        public static void TestNextFromNothingStartsAtTheTop()
        {
            var list = Three();
            Assert.Equal(-1, list.CurrentIndex, "nothing selected yet");
            Assert.Equal(0, list.Next(false), "the first press starts the list");
        }

        public static void TestShuffleVisitsEveryTrackOnce()
        {
            var list = new Playlist();
            var paths = new List<string>();
            for (int i = 0; i < 25; i++) paths.Add(@"C:\t" + i + ".mp3");
            list.Add(paths);
            list.Shuffle = true;
            list.Repeat = RepeatMode.Off;

            var seen = new HashSet<int>();
            int at = list.Next(true);
            while (at >= 0 && seen.Count <= paths.Count)
            {
                Assert.True(seen.Add(at), "shuffle visited " + at + " twice in one pass");
                list.CurrentIndex = at;
                at = list.Next(true);
            }
            Assert.Equal(paths.Count, seen.Count, "every track was played exactly once");
        }

        public static void TestRemoveKeepsPlayingTrack()
        {
            // Deleting rows above the playing one must not silently switch what is
            // playing to whatever slid into its index.
            var list = Three();
            list.CurrentIndex = 2;
            list.Remove(new List<int> { 0 });
            Assert.Equal(2, list.Count, "one row went");
            Assert.Equal(1, list.CurrentIndex, "the index followed the track");
            Assert.Equal(@"C:\c.mp3", list.Current.Path, "and it is still the same file");
        }

        public static void TestRemovingThePlayingTrackClearsCurrent()
        {
            var list = Three();
            list.CurrentIndex = 1;
            list.Remove(new List<int> { 1 });
            Assert.Equal(-1, list.CurrentIndex, "nothing is playing any more");
        }

        public static void TestRemoveIgnoresOutOfRange()
        {
            var list = Three();
            list.Remove(new List<int> { 99, -1 });
            Assert.Equal(3, list.Count, "nonsense indices are dropped, not thrown on");
        }

        public static void TestClear()
        {
            var list = Three();
            list.CurrentIndex = 1;
            list.Clear();
            Assert.Equal(0, list.Count, "empty");
            Assert.Equal(-1, list.CurrentIndex, "and nothing selected");
        }

        public static void TestCurrentIndexRejectsOutOfRange()
        {
            var list = Three();
            list.CurrentIndex = 99;
            Assert.Equal(-1, list.CurrentIndex, "an impossible index becomes none");
        }

        public static void TestTotalDuration()
        {
            var list = Three();
            list.At(0).Duration = 10;
            list.At(1).Duration = 20.5;
            Assert.Close(30.5, list.TotalDuration, 1e-9, "durations add up");
        }

        public static void TestSnapshotIsACopy()
        {
            var list = Three();
            Track[] snapshot = list.Snapshot();
            list.Clear();
            Assert.Equal(3, snapshot.Length, "the copy still holds what the list held");
            Assert.Equal(0, list.Count, "and clearing the list did not reach into it");
        }

        public static void TestSnapshotSurvivesTheListChangingUnderIt()
        {
            // The tag scanner walks the playlist from a pool thread while the UI
            // thread adds to it, removes from it and clears it. Reading the live
            // list from there indexes past an end that has just moved; this runs
            // both sides at once for long enough to catch that if it comes back.
            var list = Three();
            Exception failure = null;
            bool stop = false;

            var reader = new Thread(delegate()
            {
                try
                {
                    while (!Volatile.Read(ref stop))
                    {
                        Track[] snapshot = list.Snapshot();
                        for (int i = 0; i < snapshot.Length; i++)
                            if (snapshot[i] == null)
                                throw new InvalidOperationException("a hole in the snapshot");
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            reader.IsBackground = true;
            reader.Start();

            for (int i = 0; i < 3000; i++)
            {
                list.Add(new List<string> { @"C:\x" + i + ".mp3" });
                list.Remove(new List<int> { 0 });
                if (i % 100 == 0) list.Clear();
            }
            Volatile.Write(ref stop, true);
            Assert.True(reader.Join(5000), "the reader finished");
            Assert.True(failure == null,
                failure == null ? "it never saw a torn list" : failure.Message);
        }

        public static void TestTrackLabelFallsBackToTheFilename()
        {
            var t = new Track(@"C:\music\Some_Song.mp3");
            Assert.Equal("Some Song", t.Label, "no tags: the filename stands in");
            t.Artist = "A";
            Assert.Equal("A — Some Song", t.Label, "artist and title once both are known");
            t.Title = "";
            Assert.Equal("A", t.Label, "artist alone");
        }
    }
}
