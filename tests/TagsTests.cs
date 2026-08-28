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

        // ---- How long it runs ----------------------------------------------------
        //
        // The length comes out of the same pass as the text, so a track shows its
        // duration the moment it is added rather than the first time it is played.
        // Each of these builds a header stating a length and checks the arithmetic
        // that gets back to seconds.

        /// <summary>An MPEG 1 layer III frame header at 44.1 kHz, stereo, no
        /// padding — 417 bytes long at 128 kbps.</summary>
        static byte[] MpegHeader(int kbps)
        {
            var table = new[] { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
            int index = Array.IndexOf(table, kbps);
            return new byte[]
            {
                0xFF, 0xFB,               // sync, MPEG 1, layer III, no CRC
                (byte)(index << 4),       // bitrate index, 44100, no padding
                0x00                      // stereo
            };
        }

        const int Mp3FrameBytes = 417;    // one 128 kbps frame at 44.1 kHz

        /// <summary>One whole mp3 frame: the header, the given bytes laid over the
        /// start of its payload, zeros for the rest.</summary>
        static byte[] Mp3Frame(byte[] payload)
        {
            var frame = new byte[Mp3FrameBytes];
            Array.Copy(MpegHeader(128), frame, 4);
            if (payload != null) Array.Copy(payload, 0, frame, 4, payload.Length);
            return frame;
        }

        public static void TestMp3ConstantBitrateLength()
        {
            // 160,000 bytes at 128 kbps is ten seconds. The ID3v2 tag in front of
            // it is not audio and must not be counted as any.
            var audio = new byte[160000];
            Array.Copy(MpegHeader(128), audio, 4);
            Array.Copy(MpegHeader(128), 0, audio, Mp3FrameBytes, 4);

            var b = new List<byte>();
            b.AddRange(Id3v2(3, "TIT2", "Drift"));
            b.AddRange(audio);

            double d = Read(b.ToArray()).Duration;
            Assert.True(Math.Abs(d - 10) < 0.05, "ten seconds of 128 kbps, got " + d);
        }

        public static void TestMp3XingFrameCountBeatsTheByteCount()
        {
            // The whole point of a Xing header: a variable bitrate file cannot be
            // measured by dividing its size by the first frame's bitrate, so where
            // the frame count is stated it has to win.
            var xing = new List<byte>();
            xing.AddRange(new byte[32]);                       // the side information
            xing.AddRange(Encoding.ASCII.GetBytes("Xing"));
            xing.AddRange(Int32Be(1));                         // flags: frame count present
            xing.AddRange(Int32Be(1000));

            var b = new List<byte>();
            b.AddRange(Id3v2(3, "TIT2", "Drift"));
            b.AddRange(Mp3Frame(xing.ToArray()));
            b.AddRange(Mp3Frame(null));

            double d = Read(b.ToArray()).Duration;
            Assert.True(Math.Abs(d - 1000 * 1152.0 / 44100) < 0.01,
                "a thousand frames, got " + d);
        }

        public static void TestMp3IgnoresTheId3v1BlockAtTheEnd()
        {
            // 128 bytes of tag counted as audio is an extra eight milliseconds on
            // every stripped mp3 — small, but it is the same mistake that makes a
            // cover-art tag add a whole second at the front.
            var audio = new byte[128000];                      // eight seconds
            Array.Copy(MpegHeader(128), audio, 4);
            Array.Copy(MpegHeader(128), 0, audio, Mp3FrameBytes, 4);

            var b = new List<byte>();
            b.AddRange(audio);
            b.AddRange(Id3v1("Drift", "Low Orbit", "Night Transit", "2019"));

            double d = Read(b.ToArray()).Duration;
            Assert.True(Math.Abs(d - 8) < 0.01, "eight seconds and no tag, got " + d);
        }

        public static void TestMp3RefusesAFalseSync()
        {
            // 0xFF 0xFF is a sync pattern followed by a reserved version and a
            // reserved layer. Believing it would produce a length out of nothing.
            var b = new List<byte>();
            b.AddRange(Id3v2(3, "TIT2", "Drift"));
            for (int i = 0; i < 64; i++) b.Add(0xFF);
            Assert.Equal(0.0, Read(b.ToArray()).Duration, "junk is not a frame");
        }

        /// <summary>A FLAC file whose STREAMINFO states the rate and the sample
        /// count, which is where its length comes from.</summary>
        static byte[] FlacWithStreamInfo(int rate, long samples)
        {
            var info = new byte[34];
            info[10] = (byte)(rate >> 12);
            info[11] = (byte)(rate >> 4);
            // Four low bits of the rate, then three of channel count and the top
            // bit of the sample depth, all packed into one byte.
            info[12] = (byte)(((rate & 0xF) << 4) | (1 << 1));
            info[13] = (byte)((15 << 4) | ((samples >> 32) & 0x0F));   // 16-bit samples
            info[14] = (byte)(samples >> 24);
            info[15] = (byte)(samples >> 16);
            info[16] = (byte)(samples >> 8);
            info[17] = (byte)samples;

            var b = new List<byte>();
            b.AddRange(Encoding.ASCII.GetBytes("fLaC"));
            b.Add(0x00);                       // STREAMINFO, and not the last block
            b.Add(0); b.Add(0); b.Add(34);
            b.AddRange(info);
            b.Add(0x84);                       // VORBIS_COMMENT, flagged last
            b.Add(0); b.Add(0); b.Add(0);
            return b.ToArray();
        }

        public static void TestFlacLengthFromStreamInfo()
        {
            double d = Read(FlacWithStreamInfo(44100, 44100L * 251)).Duration;
            Assert.True(Math.Abs(d - 251) < 0.001, "four minutes eleven, got " + d);
        }

        public static void TestFlacUnknownSampleCountStaysUnknown()
        {
            // Zero is the format's own "I was written to a pipe and do not know",
            // not a track of no length.
            Assert.Equal(0.0, Read(FlacWithStreamInfo(44100, 0)).Duration,
                "an unstated sample count");
        }

        static byte[] OggPage(long granule, byte[] payload)
        {
            var b = new List<byte>();
            b.AddRange(Encoding.ASCII.GetBytes("OggS"));
            b.Add(0);                          // version
            b.Add(0);                          // header type
            for (int i = 0; i < 8; i++) b.Add((byte)(granule >> (8 * i)));
            b.AddRange(LittleInt(1));          // stream serial
            b.AddRange(LittleInt(0));          // page sequence
            b.AddRange(LittleInt(0));          // checksum, which nothing here verifies
            b.Add(1);                          // one segment
            b.Add((byte)payload.Length);
            b.AddRange(payload);
            return b.ToArray();
        }

        static byte[] VorbisIdent(int rate)
        {
            var b = new List<byte>();
            b.Add(1);
            b.AddRange(Encoding.ASCII.GetBytes("vorbis"));
            b.AddRange(LittleInt(0));          // version
            b.Add(2);                          // channels
            b.AddRange(LittleInt(rate));
            b.AddRange(new byte[16]);          // the bitrate hints and the block sizes
            return b.ToArray();
        }

        public static void TestOggVorbisLengthFromTheLastGranule()
        {
            var b = new List<byte>();
            b.AddRange(OggPage(0, VorbisIdent(44100)));
            b.AddRange(OggPage(44100L * 5, new byte[64]));
            double d = Read(b.ToArray()).Duration;
            Assert.True(Math.Abs(d - 5) < 0.001, "five seconds of vorbis, got " + d);
        }

        public static void TestOggSkipsAPageThatFinishesNoPacket()
        {
            // All ones is the container's "nothing ends here". Read as a number it
            // is a length of six million years.
            var b = new List<byte>();
            b.AddRange(OggPage(0, VorbisIdent(48000)));
            b.AddRange(OggPage(48000L * 3, new byte[32]));
            b.AddRange(OggPage(-1, new byte[32]));
            double d = Read(b.ToArray()).Duration;
            Assert.True(Math.Abs(d - 3) < 0.001, "the last page that ended a packet, got " + d);
        }

        public static void TestOpusLengthDropsThePreSkip()
        {
            // Opus counts granules at 48 kHz whatever the source rate was, and the
            // pre-skip in front of them is the encoder's padding, not music.
            var head = new List<byte>();
            head.AddRange(Encoding.ASCII.GetBytes("OpusHead"));
            head.Add(1);                       // version
            head.Add(2);                       // channels
            head.Add(0x38); head.Add(0x01);    // pre-skip: 312 samples
            head.AddRange(LittleInt(48000));
            head.AddRange(new byte[3]);

            var b = new List<byte>();
            b.AddRange(OggPage(0, head.ToArray()));
            b.AddRange(OggPage(48000L * 2 + 312, new byte[32]));
            double d = Read(b.ToArray()).Duration;
            Assert.True(Math.Abs(d - 2) < 0.001, "two seconds of opus, got " + d);
        }

        static byte[] Mvhd(int timescale, long duration)
        {
            var b = new List<byte>();
            b.AddRange(Int32Be(0));            // version 0, no flags
            b.AddRange(Int32Be(0));            // created
            b.AddRange(Int32Be(0));            // modified
            b.AddRange(Int32Be(timescale));
            b.AddRange(Int32Be((int)duration));
            b.AddRange(new byte[80]);          // the rate, the matrix and the rest
            return Atom("mvhd", b.ToArray());
        }

        static byte[] Mp4WithMovieHeader(byte[] mvhd)
        {
            var b = new List<byte>();
            b.AddRange(Atom("ftyp", Encoding.ASCII.GetBytes("M4A isom")));
            b.AddRange(Atom("moov", mvhd));
            return b.ToArray();
        }

        public static void TestMp4LengthFromTheMovieHeader()
        {
            double d = Read(Mp4WithMovieHeader(Mvhd(1000, 187500))).Duration;
            Assert.True(Math.Abs(d - 187.5) < 0.001, "three minutes seven and a half, got " + d);
        }

        public static void TestMp4UnknownDurationStaysUnknown()
        {
            // All ones is what a fragmented file writes when the total is not
            // known yet; at a millisecond timescale it would read as fifty days.
            double d = Read(Mp4WithMovieHeader(Mvhd(1000, 0xFFFFFFFFL))).Duration;
            Assert.Equal(0.0, d, "an unstated movie duration");
        }

        static byte[] Wav(long declaredDataSize, int byteRate, int actualDataBytes)
        {
            var b = new List<byte>();
            b.AddRange(Encoding.ASCII.GetBytes("RIFF"));
            b.AddRange(LittleInt(0));                      // the riff size, unread
            b.AddRange(Encoding.ASCII.GetBytes("WAVE"));
            b.AddRange(Encoding.ASCII.GetBytes("fmt "));
            b.AddRange(LittleInt(16));
            b.AddRange(new byte[] { 1, 0, 1, 0 });          // pcm, mono
            b.AddRange(LittleInt(byteRate));               // 8-bit at 8 kHz
            b.AddRange(LittleInt(byteRate));
            b.AddRange(new byte[] { 1, 0, 8, 0 });
            b.AddRange(Encoding.ASCII.GetBytes("data"));
            b.AddRange(LittleInt((int)declaredDataSize));
            b.AddRange(new byte[actualDataBytes]);
            return b.ToArray();
        }

        public static void TestWavLengthFromTheDataChunk()
        {
            double d = Read(Wav(16000, 8000, 16000)).Duration;
            Assert.True(Math.Abs(d - 2) < 0.001, "two seconds of pcm, got " + d);
        }

        public static void TestWavBelievesTheFileOverTheHeader()
        {
            // A wav written to a pipe carries a placeholder size. What is actually
            // in the file is the only honest answer.
            double d = Read(Wav(int.MaxValue, 8000, 8000)).Duration;
            Assert.True(Math.Abs(d - 1) < 0.001, "one second really present, got " + d);
        }

        static byte[] Asf(long playHundredNs, long prerollMs)
        {
            var b = new List<byte>();
            // The header object: its GUID, a size, an object count and two spare
            // bytes, none of which the reader looks at beyond the first four.
            b.AddRange(new byte[]
            {
                0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
                0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
            });
            b.AddRange(new byte[8]);           // size
            b.AddRange(LittleInt(1));          // one child object
            b.Add(1); b.Add(2);                // reserved

            b.AddRange(new byte[]              // ASF_File_Properties_Object
            {
                0xA1, 0xDC, 0xAB, 0x8C, 0x47, 0xA9, 0xCF, 0x11,
                0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
            });
            b.AddRange(new byte[8]);           // object size
            b.AddRange(new byte[16]);          // file id
            b.AddRange(new byte[8]);           // file size
            b.AddRange(new byte[8]);           // creation date
            b.AddRange(new byte[8]);           // data packet count
            b.AddRange(Little64(playHundredNs));
            b.AddRange(new byte[8]);           // send duration
            b.AddRange(Little64(prerollMs));
            b.AddRange(new byte[24]);          // flags and the packet sizes
            return b.ToArray();
        }

        static byte[] Little64(long v)
        {
            var b = new byte[8];
            for (int i = 0; i < 8; i++) b[i] = (byte)(v >> (8 * i));
            return b;
        }

        public static void TestWmaLengthDropsThePreroll()
        {
            // The play duration includes the preroll — the buffer the player is
            // told to fill before it starts, which is not part of the music.
            double d = Read(Asf(40000000L, 1000)).Duration;
            Assert.True(Math.Abs(d - 3) < 0.001, "three seconds of wma, got " + d);
        }

        public static void TestAnUnknownContainerHasNoLength()
        {
            // Matroska is not parsed here, and saying so honestly is what lets the
            // decoder fill it in when the track starts.
            var b = new List<byte>();
            b.AddRange(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 });
            b.AddRange(new byte[64]);
            Assert.Equal(0.0, Read(b.ToArray()).Duration, "an unparsed container");
        }
    }
}
