// Formatting, the settings parser and the M3U round trip.
using System;
using System.Collections.Generic;

namespace Retrace.Tests
{
    public static class UtilTests
    {
        public static void TestTimeFormatsShortAndLong()
        {
            Assert.Equal("0:00", Util.Time(0), "zero");
            Assert.Equal("0:07", Util.Time(7), "seconds pad to two digits");
            Assert.Equal("3:04", Util.Time(184), "minutes do not pad");
            Assert.Equal("1:00:00", Util.Time(3600), "an hour grows a field");
            Assert.Equal("2:03:04", Util.Time(7384), "hours, minutes, seconds");
        }

        public static void TestTimeSurvivesRubbish()
        {
            // Duration comes from a container that may not declare one, and a
            // NaN reaching the display must not throw inside a paint handler.
            Assert.Equal("0:00", Util.Time(double.NaN), "NaN");
            Assert.Equal("0:00", Util.Time(double.PositiveInfinity), "infinity");
            Assert.Equal("0:00", Util.Time(-5), "negative");
        }

        public static void TestKhz()
        {
            Assert.Equal("44.1k", Util.Khz(44100), "44.1 keeps its decimal");
            Assert.Equal("48k", Util.Khz(48000), "a whole rate drops it");
            Assert.Equal("", Util.Khz(0), "unknown rate prints nothing");
        }

        public static void TestIsAudioFile()
        {
            Assert.True(Util.IsAudioFile(@"C:\music\a.mp3"), "mp3");
            Assert.True(Util.IsAudioFile(@"C:\music\A.FLAC"), "extension match is case-insensitive");
            Assert.False(Util.IsAudioFile(@"C:\music\a.txt"), "txt");
            Assert.False(Util.IsAudioFile(@"C:\music\noextension"), "no extension");
            Assert.False(Util.IsAudioFile(null), "null");
            Assert.False(Util.IsAudioFile(""), "empty");
        }

        public static void TestParseIniIgnoresJunk()
        {
            var map = Util.ParseIni("# a comment\n; another\n[section]\nvolume=0.5\nbroken\n=novalue\n"
                + "  spaced  =  7  \n");
            Assert.Equal("0.5", Util.IniString(map, "volume", ""), "value");
            Assert.Equal("7", Util.IniString(map, "spaced", ""), "keys and values are trimmed");
            Assert.Equal(2, map.Count, "comments, sections and malformed lines are dropped");
        }

        public static void TestIniValuesAreRangeChecked()
        {
            var map = Util.ParseIni("a=999\nb=-999\nc=notanumber\nd=1\n");
            Assert.Equal(10, Util.IniInt(map, "a", 5, 0, 10), "above the ceiling clamps");
            Assert.Equal(0, Util.IniInt(map, "b", 5, 0, 10), "below the floor clamps");
            Assert.Equal(5, Util.IniInt(map, "c", 5, 0, 10), "unparseable falls back");
            Assert.Equal(5, Util.IniInt(map, "missing", 5, 0, 10), "absent falls back");
            Assert.True(Util.IniBool(map, "d", false), "1 is true");
            Assert.False(Util.IniBool(map, "missing", false), "absent bool falls back");
        }

        public static void TestIniDoubleIsCultureInvariant()
        {
            // The file is written with an invariant point and must read back the
            // same on a machine whose locale uses a comma.
            var map = Util.ParseIni("volume=0.35\n");
            Assert.Close(0.35, Util.IniDouble(map, "volume", 1, 0, 1), 1e-9, "invariant decimal point");
            Assert.Equal("0.35", Util.Num(0.35), "and is written back the same way");
        }

        public static void TestIniDoubleRejectsNonFinite()
        {
            var map = Util.ParseIni("a=NaN\nb=Infinity\n");
            Assert.Close(0.5, Util.IniDouble(map, "a", 0.5, 0, 1), 1e-9, "NaN falls back");
            Assert.Close(0.5, Util.IniDouble(map, "b", 0.5, 0, 1), 1e-9, "infinity falls back");
        }

        public static void TestParseM3uResolvesRelativePaths()
        {
            var list = Util.ParseM3u("#EXTM3U\n#EXTINF:12,Song\nsub\\a.mp3\nC:\\abs\\b.mp3\n",
                @"C:\base");
            Assert.Equal(2, list.Count, "comments are skipped");
            Assert.Equal(@"C:\base\sub\a.mp3", list[0], "relative resolves against the playlist");
            Assert.Equal(@"C:\abs\b.mp3", list[1], "absolute is left alone");
        }

        public static void TestParseM3uSkipsUrls()
        {
            // A stream entry is not something this player can open, and handing a
            // nonsense path to the decoder is worse than dropping the line.
            var list = Util.ParseM3u("http://example.com/a.mp3\nlocal.mp3\n", @"C:\base");
            Assert.Equal(1, list.Count, "the URL is dropped");
            Assert.Equal(@"C:\base\local.mp3", list[0], "the local entry survives");
        }

        public static void TestM3uRoundTrip()
        {
            var paths = new List<string> { @"C:\a.mp3", @"C:\b.mp3" };
            var titles = new List<string> { "One", "Two" };
            var durations = new List<double> { 61.4, 0 };
            string text = Util.BuildM3u(paths, titles, durations);
            var back = Util.ParseM3u(text, @"C:\");
            Assert.Equal(2, back.Count, "both entries come back");
            Assert.Equal(@"C:\a.mp3", back[0], "first path");
            Assert.Equal(@"C:\b.mp3", back[1], "second path");
            Assert.True(text.StartsWith("#EXTM3U"), "carries the header");
            Assert.True(text.Contains("#EXTINF:61,One"), "duration truncates to whole seconds");
        }

        public static void TestFit()
        {
            Assert.Equal("abc", Util.Fit("abc", 5), "shorter than the field is untouched");
            Assert.Equal("ab…", Util.Fit("abcdef", 3), "longer is cut and marked");
            Assert.Equal("", Util.Fit(null, 5), "null");
            Assert.Equal("", Util.Fit("abc", 0), "no room at all");
        }

        public static void TestTitleFromPath()
        {
            Assert.Equal("Song Name", Util.TitleFromPath(@"C:\x\Song_Name.mp3"),
                "underscores stand in for spaces in ripped filenames");
            Assert.Equal("Plain", Util.TitleFromPath(@"C:\x\Plain.flac"), "extension is dropped");
            Assert.Equal("", Util.TitleFromPath(null), "null");
        }

        public static void TestExpandIgnoresMissingPaths()
        {
            // Files handed over by a drop or the command line may not exist by the
            // time they are opened; that is a skip, not an exception.
            var list = Util.Expand(new List<string>
                { @"C:\definitely\not\here.mp3", null, "" });
            Assert.Equal(0, list.Count, "nothing playable was found");
        }
    }
}
