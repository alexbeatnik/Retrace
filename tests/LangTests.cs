// The string table. Property tests over the whole thing rather than hand-picked
// cases: a key added to one language and forgotten in the other then fails on
// arrival instead of showing up as an English word in a Ukrainian window.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Retrace.Tests
{
    public static class LangTests
    {
        public static void TestBothLanguagesHaveTheSameKeys()
        {
            foreach (string key in Lang.En.Keys)
                Assert.True(Lang.Uk.ContainsKey(key), "uk is missing the key " + key);
            foreach (string key in Lang.Uk.Keys)
                Assert.True(Lang.En.ContainsKey(key), "en is missing the key " + key);
        }

        public static void TestNoStringIsEmpty()
        {
            foreach (KeyValuePair<string, string> pair in Lang.En)
                Assert.True(pair.Value.Trim().Length > 0, "en:" + pair.Key + " is blank");
            foreach (KeyValuePair<string, string> pair in Lang.Uk)
                Assert.True(pair.Value.Trim().Length > 0, "uk:" + pair.Key + " is blank");
        }

        public static void TestPlaceholdersAgree()
        {
            // A translation with a {0} the original does not have throws inside
            // string.Format at the moment the window opens.
            foreach (KeyValuePair<string, string> pair in Lang.En)
            {
                string uk = Lang.Uk[pair.Key];
                Assert.Equal(Placeholders(pair.Value), Placeholders(uk),
                    "placeholder mismatch on " + pair.Key);
            }
        }

        static string Placeholders(string s)
        {
            var found = new List<string>();
            foreach (Match m in Regex.Matches(s, @"\{\d+\}")) found.Add(m.Value);
            found.Sort(StringComparer.Ordinal);
            return string.Join(",", found.ToArray());
        }

        public static void TestLookupFallsBackRatherThanThrowing()
        {
            string was = Lang.Current;
            try
            {
                Lang.Current = Lang.Ukrainian;
                // An unknown key returns itself: a missing string should show up as
                // an obvious placeholder, never as a crash in a paint handler.
                Assert.Equal("no.such.key", Lang.T("no.such.key"), "unknown key");
                Assert.False(Lang.T("card.playlist") == "card.playlist", "a known key resolves");
            }
            finally { Lang.Current = was; }
        }

        public static void TestCurrentRejectsUnknownLanguages()
        {
            string was = Lang.Current;
            try
            {
                Lang.Current = "de";
                Assert.Equal(Lang.English, Lang.Current, "an unsupported code falls back to English");
            }
            finally { Lang.Current = was; }
        }

        public static void TestUkrainianIsActuallyTranslated()
        {
            // Catches a block pasted across without being translated. Keys whose
            // value is a proper noun or a shared abbreviation are exempt.
            var sameOnPurpose = new HashSet<string>
            {
                // Proper nouns, and the file-extension list, which is not language.
                "menu.english", "menu.ukrainian", "list.formats"
            };
            int identical = 0;
            foreach (KeyValuePair<string, string> pair in Lang.En)
                if (!sameOnPurpose.Contains(pair.Key) && Lang.Uk[pair.Key] == pair.Value)
                    identical++;
            Assert.True(identical == 0, identical + " Ukrainian strings are still the English ones");
        }
    }
}
