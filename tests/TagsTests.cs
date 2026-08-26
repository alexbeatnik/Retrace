// The tag readers, driven from tags built in memory.
//
// Every length in these formats comes out of the file itself, so as well as the
// happy paths these check that a truncated or lying header is refused rather
// than walked off the end of the buffer.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Retrace.Tests
{
    public static class TagsTests
    {
        // ---- Builders ----------------------------------------------------------

        static byte[] SyncSafe(int value)
        {
            return new byte[]
            {
                (byte)((value >> 21) & 0x7F), (byte)((value >> 14) & 0x7F),
                (byte)((value >> 7) & 0x7F), (byte)(value & 0x7F)
            };
        }

        static byte[] Int32Be(int value)
        {
            return new byte[]
            {
                (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
            };
        }

        /// <summary>An ID3v2 tag with Latin-1 text frames, at the given major
        /// version (3 uses a plain size, 4 a sync-safe one).</summary>
        static byte[] Id3v2(int major, params string[] idsAndValues)
        {
            var frames = new List<byte>();
            for (int i = 0; i < idsAndValues.Length; i += 2)
            {
                byte[] text = Encoding.GetEncoding(28591).GetBytes(idsAndValues[i + 1]);
                frames.AddRange(Encoding.ASCII.GetBytes(idsAndValues[i]));
                int size = text.Length + 1;   // the encoding byte
                frames.AddRange(major == 4 ? SyncSafe(size) : Int32Be(size));
                frames.Add(0); frames.Add(0);  // frame flags
                frames.Add(0);                 // encoding: Latin-1
                frames.AddRange(text);
            }

            var tag = new List<byte>();
            tag.AddRange(Encoding.ASCII.GetBytes("ID3"));
            tag.Add((byte)major); tag.Add(0);
            tag.Add(0);                        // header flags
            tag.AddRange(SyncSafe(frames.Count));
            tag.AddRange(frames);
            return tag.ToArray();
        }

        static byte[] Id3v1(string title, string artist, string album, string year)
        {
            var b = new byte[128];
            Encoding.ASCII.GetBytes("TAG").CopyTo(b, 0);
            Write(b, 3, title, 30);
            Write(b, 33, artist, 30);
            Write(b, 63, album, 30);
            Write(b, 93, year, 4);
            return b;
        }

        static void Write(byte[] b, int at, string s, int len)
        {
            byte[] bytes = Encoding.GetEncoding(28591).GetBytes(s);
            Array.Copy(bytes, 0, b, at, Math.Min(bytes.Length, len));
        }

        static byte[] LittleInt(int v)
        {
            return new byte[] { (byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24) };
        }

        static byte[] VorbisComments(params string[] entries)
        {
            var b = new List<byte>();
            byte[] vendor = Encoding.UTF8.GetBytes("test");
            b.AddRange(LittleInt(vendor.Length));
            b.AddRange(vendor);
            b.AddRange(LittleInt(entries.Length));
            foreach (string e in entries)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(e);
                b.AddRange(LittleInt(bytes.Length));
                b.AddRange(bytes);
            }
            return b.ToArray();
        }

        static byte[] Flac(byte[] comments)
        {
            var b = new List<byte>();
            b.AddRange(Encoding.ASCII.GetBytes("fLaC"));
            // One VORBIS_COMMENT block (type 4), flagged last.
            b.Add(0x84);
            b.Add((byte)(comments.Length >> 16));
            b.Add((byte)(comments.Length >> 8));
            b.Add((byte)comments.Length);
            b.AddRange(comments);
            // Enough padding that the 12-byte magic read at the top has something
            // to look at even for a tiny tag.
            b.AddRange(new byte[16]);
            return b.ToArray();
        }

        static byte[] Atom(string name, byte[] body)
        {
            var b = new List<byte>();
            b.AddRange(Int32Be(body.Length + 8));
            // Latin-1 so the 0xA9 of an iTunes atom name is written as that byte
            // rather than as the '?' an ASCII encode would substitute.
            b.AddRange(Encoding.GetEncoding(28591).GetBytes(name));
            b.AddRange(body);
            return b.ToArray();
        }

        static byte[] TextTag(string name, string value)
        {
            var data = new List<byte>();
            data.AddRange(Int32Be(1));         // type: UTF-8 text
            data.AddRange(Int32Be(0));         // locale
            data.AddRange(Encoding.UTF8.GetBytes(value));
            return Atom(name, Atom("data", data.ToArray()));
        }

        static byte[] Mp4(params byte[][] tags)
        {
            var ilst = new List<byte>();
            foreach (byte[] t in tags) ilst.AddRange(t);

            var meta = new List<byte>();
            meta.AddRange(Int32Be(0));         // meta is a full atom: version + flags
            meta.AddRange(Atom("ilst", ilst.ToArray()));

            var b = new List<byte>();
            b.AddRange(Atom("ftyp", Encoding.ASCII.GetBytes("M4A isom")));
            b.AddRange(Atom("moov", Atom("udta", Atom("meta", meta.ToArray()))));
            return b.ToArray();
        }

        static TrackTags Read(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes)) return Tags.Read(ms);
        }

        // ---- ID3v2 ---------------------------------------------------------------

        public static void TestId3v23()
        {
            TrackTags t = Read(Id3v2(3,
                "TPE1", "Kraftwerk", "TIT2", "Autobahn", "TALB", "Autobahn", "TYER", "1974"));
            Assert.Equal("Kraftwerk", t.Artist, "artist");
            Assert.Equal("Autobahn", t.Title, "title");
            Assert.Equal("Autobahn", t.Album, "album");
            Assert.Equal("1974", t.Year, "year");
        }

        public static void TestId3v24UsesSyncSafeFrameSizes()
        {
            TrackTags t = Read(Id3v2(4, "TPE1", "Boards of Canada", "TIT2", "Roygbiv"));
            Assert.Equal("Boards of Canada", t.Artist, "artist");
            Assert.Equal("Roygbiv", t.Title, "title");
        }

        public static void TestId3v24TimestampReducesToAYear()
        {
            TrackTags t = Read(Id3v2(4, "TDRC", "1998-04-20T12:00:00"));
            Assert.Equal("1998", t.Year, "only the year is shown");
        }

        public static void TestImplausibleYearIsDropped()
        {
            Assert.Equal("", Read(Id3v2(3, "TYER", "0000")).Year, "too early");
            Assert.Equal("", Read(Id3v2(3, "TYER", "abcd")).Year, "not a number");
        }

        public static void TestUtf16FrameWithBom()
        {
            // Encoding 1 is UTF-16 with a byte-order mark, which is what almost
            // every Windows tagger writes.
            var frames = new List<byte>();
            byte[] text = Encoding.Unicode.GetPreamble();
            byte[] body = Encoding.Unicode.GetBytes("Björk");
            frames.AddRange(Encoding.ASCII.GetBytes("TPE1"));
            frames.AddRange(Int32Be(text.Length + body.Length + 1));
            frames.Add(0); frames.Add(0);
            frames.Add(1);                     // encoding: UTF-16
            frames.AddRange(text);
            frames.AddRange(body);

            var tag = new List<byte>();
            tag.AddRange(Encoding.ASCII.GetBytes("ID3"));
            tag.Add(3); tag.Add(0); tag.Add(0);
            tag.AddRange(SyncSafe(frames.Count));
            tag.AddRange(frames);

            Assert.Equal("Björk", Read(tag.ToArray()).Artist, "UTF-16 decodes");
        }

        public static void TestId3v22ThreeCharacterFrames()
        {
            var frames = new List<byte>();
            byte[] text = Encoding.ASCII.GetBytes("Pixies");
            frames.AddRange(Encoding.ASCII.GetBytes("TP1"));
            int size = text.Length + 1;
            frames.Add((byte)(size >> 16)); frames.Add((byte)(size >> 8)); frames.Add((byte)size);
            frames.Add(0);
            frames.AddRange(text);

            var tag = new List<byte>();
            tag.AddRange(Encoding.ASCII.GetBytes("ID3"));
            tag.Add(2); tag.Add(0); tag.Add(0);
            tag.AddRange(SyncSafe(frames.Count));
            tag.AddRange(frames);
            tag.AddRange(new byte[16]);

            Assert.Equal("Pixies", Read(tag.ToArray()).Artist, "the older three-letter frame ids");
        }

        // ---- ID3v1 ---------------------------------------------------------------

        public static void TestId3v1()
        {
            var file = new List<byte>();
            file.AddRange(new byte[512]);      // stand-in audio
            file.AddRange(Id3v1("Song", "Artist", "Album", "1999"));
            TrackTags t = Read(file.ToArray());
            Assert.Equal("Artist", t.Artist, "artist");
            Assert.Equal("Song", t.Title, "title");
            Assert.Equal("1999", t.Year, "year");
        }

        public static void TestId3v2WinsOverV1()
        {
            // A file with both carries the long, correctly-encoded strings in v2;
            // v1 is a truncated fallback and must not overwrite them.
            var file = new List<byte>(Id3v2(3, "TPE1", "The Full Artist Name"));
            file.AddRange(new byte[256]);
            file.AddRange(Id3v1("", "Truncated", "", ""));
            Assert.Equal("The Full Artist Name", Read(file.ToArray()).Artist, "v2 wins");
        }

        public static void TestId3v1FillsGapsLeftByV2()
        {
            var file = new List<byte>(Id3v2(3, "TPE1", "Only The Artist"));
            file.AddRange(new byte[256]);
            file.AddRange(Id3v1("A Title", "", "", ""));
            TrackTags t = Read(file.ToArray());
            Assert.Equal("Only The Artist", t.Artist, "v2 artist stands");
            Assert.Equal("A Title", t.Title, "and v1 supplies the missing title");
        }

        // ---- FLAC and Ogg -----------------------------------------------------------

        public static void TestFlacVorbisComments()
        {
            TrackTags t = Read(Flac(VorbisComments(
                "ARTIST=Aphex Twin", "TITLE=Xtal", "ALBUM=Selected Ambient Works",
                "DATE=1992")));
            Assert.Equal("Aphex Twin", t.Artist, "artist");
            Assert.Equal("Xtal", t.Title, "title");
            Assert.Equal("Selected Ambient Works", t.Album, "album");
            Assert.Equal("1992", t.Year, "year");
        }

        public static void TestVorbisFieldNamesAreCaseInsensitive()
        {
            // The spec says field names are case-insensitive and taggers take it
            // at its word.
            TrackTags t = Read(Flac(VorbisComments("artist=Lowercase", "Title=Mixed")));
            Assert.Equal("Lowercase", t.Artist, "artist");
            Assert.Equal("Mixed", t.Title, "title");
        }

        public static void TestVorbisUtf8()
        {
            TrackTags t = Read(Flac(VorbisComments("ARTIST=Сергій Жадан", "TITLE=Пісня")));
            Assert.Equal("Сергій Жадан", t.Artist, "Cyrillic survives the round trip");
            Assert.Equal("Пісня", t.Title, "title");
        }

        public static void TestOggOpusTags()
        {
            var b = new List<byte>();
            b.AddRange(Encoding.ASCII.GetBytes("OggS"));
            b.AddRange(new byte[40]);
            b.AddRange(Encoding.ASCII.GetBytes("OpusTags"));
            b.AddRange(VorbisComments("ARTIST=Opus Artist", "TITLE=Opus Title"));
            TrackTags t = Read(b.ToArray());
            Assert.Equal("Opus Artist", t.Artist, "artist");
            Assert.Equal("Opus Title", t.Title, "title");
        }

        // ---- MP4 -------------------------------------------------------------------

        public static void TestMp4ITunesTags()
        {
            TrackTags t = Read(Mp4(
                TextTag("©ART", "Massive Attack"),
                TextTag("©nam", "Teardrop"),
                TextTag("©alb", "Mezzanine"),
                TextTag("©day", "1998")));
            Assert.Equal("Massive Attack", t.Artist, "artist");
            Assert.Equal("Teardrop", t.Title, "title");
            Assert.Equal("Mezzanine", t.Album, "album");
            Assert.Equal("1998", t.Year, "year");
        }

        public static void TestMp4NonTextDataIsIgnored()
        {
            // A track-number atom carries a byte array, not text; decoding it as
            // UTF-8 would put control characters on the display.
            var data = new List<byte>();
            data.AddRange(Int32Be(0));         // type 0: binary
            data.AddRange(Int32Be(0));
            data.AddRange(new byte[] { 0, 1, 0, 2 });
            byte[] trkn = Atom("trkn", Atom("data", data.ToArray()));
            TrackTags t = Read(Mp4(trkn, TextTag("©nam", "Real Title")));
            Assert.Equal("Real Title", t.Title, "the text tag still reads");
        }

        // ---- Rubbish in --------------------------------------------------------------

        public static void TestUntaggedFileIsEmptyNotAnError()
        {
            TrackTags t = Read(new byte[4096]);
            Assert.True(t.IsEmpty, "no tags is a normal outcome");
        }

        public static void TestEmptyAndTinyStreams()
        {
            Assert.True(Read(new byte[0]).IsEmpty, "empty file");
            Assert.True(Read(new byte[3]).IsEmpty, "shorter than the magic");
        }

        public static void TestTruncatedId3IsRefused()
        {
            byte[] full = Id3v2(3, "TPE1", "Someone", "TIT2", "Something");
            for (int cut = 11; cut < full.Length; cut += 3)
            {
                var partial = new byte[cut];
                Array.Copy(full, partial, cut);
                Read(partial);   // must not throw at any truncation point
            }
        }

        public static void TestLyingFrameSizeIsRefused()
        {
            // A frame that claims to be longer than the tag would run the parser
            // off the end of the buffer.
            var frames = new List<byte>();
            frames.AddRange(Encoding.ASCII.GetBytes("TPE1"));
            frames.AddRange(Int32Be(0x7000000));   // absurd
            frames.Add(0); frames.Add(0);
            frames.Add(0);
            frames.AddRange(Encoding.ASCII.GetBytes("short"));

            var tag = new List<byte>();
            tag.AddRange(Encoding.ASCII.GetBytes("ID3"));
            tag.Add(3); tag.Add(0); tag.Add(0);
            tag.AddRange(SyncSafe(frames.Count));
            tag.AddRange(frames);
            Assert.True(Read(tag.ToArray()).IsEmpty, "the impossible frame is dropped");
        }

        public static void TestLyingVorbisCountIsRefused()
        {
            var b = new List<byte>();
            byte[] vendor = Encoding.UTF8.GetBytes("v");
            b.AddRange(LittleInt(vendor.Length));
            b.AddRange(vendor);
            b.AddRange(LittleInt(1000000));    // more entries than there are bytes
            b.AddRange(LittleInt(4));
            b.AddRange(Encoding.UTF8.GetBytes("A=B"));
            Read(Flac(b.ToArray()));           // must not throw
        }

        public static void TestNegativeVorbisLengthIsRefused()
        {
            var b = new List<byte>();
            b.AddRange(LittleInt(0));          // no vendor string
            b.AddRange(LittleInt(1));
            b.AddRange(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });   // a size with the top bit set
            b.AddRange(new byte[8]);
            Read(Flac(b.ToArray()));           // must not throw or read backwards
        }

        public static void TestMp4WithNoMetadataAtoms()
        {
            var b = new List<byte>();
            b.AddRange(Atom("ftyp", Encoding.ASCII.GetBytes("M4A isom")));
            b.AddRange(Atom("mdat", new byte[64]));
            Assert.True(Read(b.ToArray()).IsEmpty, "a file with only media data");
        }

        public static void TestMp4ZeroSizedAtomDoesNotLoop()
        {
            // An atom claiming a size of zero means "to the end of the file"; read
            // as a step it would leave the walk going round for ever.
            var b = new List<byte>();
            b.AddRange(Atom("ftyp", Encoding.ASCII.GetBytes("M4A isom")));
            b.AddRange(Int32Be(0));
            b.AddRange(Encoding.ASCII.GetBytes("free"));
            b.AddRange(new byte[32]);
            Read(b.ToArray());   // must return rather than hang
        }
    }
}
