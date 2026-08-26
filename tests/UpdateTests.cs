// The self-updater's two pure decisions — when to check, and whether what the
// API reported is actually newer — plus the path test the installer uses to
// tell an installed copy from a portable one.
using System;
using System.IO;

namespace Retrace.Tests
{
    static class UpdateScheduleTests
    {
        static readonly DateTime Now = new DateTime(2026, 8, 26, 12, 0, 0);
        const int Period = 24;

        // Every launch gets one check regardless of when the last one happened:
        // a machine that is only ever switched on for an hour a day would
        // otherwise never come due.
        public static void TestFirstCheckOfALaunchAlwaysFires()
        {
            Assert.True(MainForm.AppUpdateDue(false, Now.AddMinutes(-1), Now, Period),
                "the startup check runs even one minute after the last one");
        }

        public static void TestNeverCheckedIsDue()
        {
            Assert.True(MainForm.AppUpdateDue(true, DateTime.MinValue, Now, Period),
                "no recorded check means check now");
        }

        public static void TestDailyBoundary()
        {
            Assert.False(MainForm.AppUpdateDue(true, Now.AddHours(-23), Now, Period),
                "23 hours is not a day");
            Assert.True(MainForm.AppUpdateDue(true, Now.AddHours(-24), Now, Period),
                "24 hours is");
        }

        // A restored VM snapshot or a timezone change must not park the check
        // until the clock catches up.
        public static void TestFutureTimestampIsDue()
        {
            Assert.True(MainForm.AppUpdateDue(true, Now.AddDays(2), Now, Period),
                "a timestamp from the future means the clock moved, not that we are early");
        }
    }

    static class UpdateVersionTests
    {
        public static void TestNewerVersionWins()
        {
            Assert.True(MainForm.IsNewerVersion("1.0.1", "1.0.0"), "patch bump");
            Assert.True(MainForm.IsNewerVersion("1.1.0", "1.0.9"), "minor bump");
            Assert.True(MainForm.IsNewerVersion("2.0.0", "1.9.9"), "major bump");
        }

        // The check runs on every launch, so "same version" has to be the common
        // case that does nothing — an off-by-one here is an infinite update loop
        // that reinstalls the same build forever.
        public static void TestSameOrOlderVersionIsIgnored()
        {
            Assert.False(MainForm.IsNewerVersion("1.0.0", "1.0.0"),
                "the running build is not an update");
            Assert.False(MainForm.IsNewerVersion("0.9.9", "1.0.0"), "nor is an older one");
        }

        public static void TestUnparseableTagsNeverTriggerADownload()
        {
            Assert.False(MainForm.IsNewerVersion("nightly", "1.0.0"), "a named tag");
            Assert.False(MainForm.IsNewerVersion("1.1.0-beta", "1.0.0"), "a pre-release suffix");
            Assert.False(MainForm.IsNewerVersion("", "1.0.0"), "an empty tag");
            Assert.False(MainForm.IsNewerVersion(null, "1.0.0"), "no tag at all");
        }

        // Brand.Version is what every comparison above is really run against, and
        // a tag the API returns is a plain dotted number. If the constant ever
        // stops parsing, the updater silently never updates again.
        public static void TestTheShippedVersionParses()
        {
            Assert.True(MainForm.IsNewerVersion("99.0.0", Brand.Version),
                "Brand.Version has to be a dotted number for the comparison to work");
        }
    }

    static class InstallPathTests
    {
        // What tells an installed copy from a portable one. A prefix compare
        // without the separator would call C:\Programs\RetraceBackup "installed".
        public static void TestExactFolderCounts()
        {
            Assert.True(MainForm.IsUnder(@"C:\Programs\Retrace", @"C:\Programs\Retrace"),
                "the folder itself is under itself");
        }

        public static void TestFileInsideCounts()
        {
            Assert.True(MainForm.IsUnder(@"C:\Programs\Retrace\Retrace.exe", @"C:\Programs\Retrace"),
                "the exe in the install folder");
        }

        public static void TestSiblingWithASharedPrefixDoesNot()
        {
            Assert.False(MainForm.IsUnder(@"C:\Programs\RetraceBackup\Retrace.exe", @"C:\Programs\Retrace"),
                "a longer name starting with the same letters is a different folder");
        }

        public static void TestCaseAndSeparatorsAreIgnored()
        {
            Assert.True(MainForm.IsUnder(@"c:\programs\retrace\sub\Retrace.exe", @"C:\Programs\Retrace\"),
                "Windows paths are case-insensitive and a trailing slash means nothing");
        }

        public static void TestRubbishIsNotUnderAnything()
        {
            Assert.False(MainForm.IsUnder(null, @"C:\Programs\Retrace"), "no path");
            Assert.False(MainForm.IsUnder("", @"C:\Programs\Retrace"), "an empty path");
            Assert.False(MainForm.IsUnder(@"C:\Programs\Retrace", ""), "an empty root");
        }
    }
}
