// Reading artist, title, album and year out of a file.
//
// Media Foundation could supply these through the property store, but that
// means a second COM surface and a decoder instantiation per file — far too
// slow for a folder of a thousand tracks. Parsing the four container formats
// that carry tags in practice is both quicker and, being pure byte handling,
// something the tests can pin down without a single real audio file.
//
// Every reader here takes untrusted input: lengths come out of the file and are
// checked against what is actually there before a byte is read.
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Retrace
{
    sealed class TrackTags
    {
        public string Artist = "";
        public string Title = "";
        public string Album = "";
        public string Year = "";
        /// <summary>Seconds, or 0 when the container did not say. Read from the
        /// same pass as the text: the list has a column for it, and waiting for
        /// the decoder means every un-played row shows --:-- until it is.</summary>
        public double Duration;

        public bool IsEmpty
        {
            get
            {
                return Artist.Length == 0 && Title.Length == 0
                    && Album.Length == 0 && Year.Length == 0;
            }
        }
    }

    static class Tags
    {
        /// <summary>Never read more than this from the front of a file: an ID3v2
        /// tag with cover art can be megabytes, and none of it is wanted.</summary>
        const int MaxHeader = 512 * 1024;

        /// <summary>
        /// Reads what tags the file carries. Anything unreadable, truncated or
        /// simply untagged comes back as an empty set rather than an exception —
        /// a playlist entry with no tags is normal, not an error.
        /// </summary>
        public static TrackTags Read(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite, 8192))
                {
                    return Read(fs);
                }
            }
            catch (IOException) { return new TrackTags(); }
            catch (UnauthorizedAccessException) { return new TrackTags(); }
            catch (ArgumentException) { return new TrackTags(); }
        }

        internal static TrackTags Read(Stream s)
        {
            var tags = new TrackTags();
            if (s == null || !s.CanRead || !s.CanSeek) return tags;

            var magic = new byte[12];
            s.Position = 0;
            if (Fill(s, magic, 0, 12) < 12) return tags;

            if (magic[0] == 'I' && magic[1] == 'D' && magic[2] == '3')
            {
                ReadId3v2(s, tags);
                MpegLength(s, tags);
            }
            else if (magic[0] == 'f' && magic[1] == 'L' && magic[2] == 'a' && magic[3] == 'C')
            {
                ReadFlac(s, tags);
                FlacLength(s, tags);
            }
            else if (magic[0] == 'O' && magic[1] == 'g' && magic[2] == 'g' && magic[3] == 'S')
            {
                ReadOgg(s, tags);
                OggLength(s, tags);
            }
            else if (magic[4] == 'f' && magic[5] == 't' && magic[6] == 'y' && magic[7] == 'p')
            {
                ReadMp4(s, tags);
                Mp4Length(s, tags);
            }
            else if (Match(magic, 0, "RIFF") && Match(magic, 8, "WAVE"))
                WavLength(s, tags);
            else if (magic[0] == 0x30 && magic[1] == 0x26 && magic[2] == 0xB2 && magic[3] == 0x75)
                AsfLength(s, tags);   // wma
            else if (magic[0] == 0xFF && (magic[1] & 0xE0) == 0xE0)
                MpegLength(s, tags);  // an mp3 carrying no ID3v2 tag at all

            // ID3v1 lives in the last 128 bytes and is the only tag a plain
            // stripped mp3 has. It fills the gaps rather than overriding: where
            // both exist, v2 carries the longer, unmangled strings.
            if (tags.Artist.Length == 0 || tags.Title.Length == 0) ReadId3v1(s, tags);
            return tags;
        }

        // Stream.Read is allowed to return fewer bytes than asked for even when
        // more are coming; a single call is a bug that only shows on slow media.
        static int Fill(Stream s, byte[] buf, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = s.Read(buf, offset + total, count - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }

        // ---- ID3v2 (mp3, and occasionally others) ---------------------------

        static void ReadId3v2(Stream s, TrackTags tags)
        {
            var head = new byte[10];
            s.Position = 0;
            if (Fill(s, head, 0, 10) < 10) return;

            int major = head[3];
            if (major < 2 || major > 4) return;
            bool unsync = (head[5] & 0x80) != 0;
            bool extended = (head[5] & 0x40) != 0;
            int size = SyncSafe(head, 6);
            if (size <= 0 || size > MaxHeader) size = Math.Min(Math.Max(size, 0), MaxHeader);
            if (size <= 0) return;

            var body = new byte[size];
            int have = Fill(s, body, 0, size);
            if (have <= 0) return;

            int p = 0;
            if (extended)
            {
                // v3 gives the extended header's size excluding its own four
                // length bytes; v4 makes it sync-safe and inclusive.
                if (have < 4) return;
                int extSize = major == 4 ? SyncSafe(body, 0) : ReadInt32(body, 0) + 4;
                if (extSize < 0 || extSize > have) return;
                p = extSize;
            }

            int idLen = major == 2 ? 3 : 4;
            int sizeLen = major == 2 ? 3 : 4;
            int flagLen = major == 2 ? 0 : 2;

            while (p + idLen + sizeLen + flagLen <= have)
            {
                string id = Encoding.ASCII.GetString(body, p, idLen);
                if (id[0] == '\0') break;   // padding: the rest of the tag is zeros

                int frameSize;
                if (major == 2) frameSize = (body[p + 3] << 16) | (body[p + 4] << 8) | body[p + 5];
                else if (major == 4) frameSize = SyncSafe(body, p + 4);
                else frameSize = ReadInt32(body, p + 4);

                int dataAt = p + idLen + sizeLen + flagLen;
                if (frameSize <= 0 || dataAt + frameSize > have) break;

                string value = null;
                if (id[0] == 'T') value = DecodeTextFrame(body, dataAt, frameSize, unsync);

                if (value != null && value.Length > 0)
                {
                    switch (id)
                    {
                        case "TPE1": case "TP1": Prefer(ref tags.Artist, value); break;
                        case "TIT2": case "TT2": Prefer(ref tags.Title, value); break;
                        case "TALB": case "TAL": Prefer(ref tags.Album, value); break;
                        case "TYER": case "TYE": case "TDRC":
                            Prefer(ref tags.Year, Year(value)); break;
                    }
                }
                p = dataAt + frameSize;
            }
        }

        static void Prefer(ref string field, string value)
        {
            if (field.Length == 0 && !string.IsNullOrEmpty(value)) field = value;
        }

        /// <summary>TDRC carries a full ISO timestamp; only the year is shown.</summary>
        static string Year(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string s = value.Trim();
            if (s.Length > 4) s = s.Substring(0, 4);
            int n;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)
                && n > 1000 && n < 3000 ? s : "";
        }

        /// <summary>
        /// A text frame: one encoding byte, then the string. The encodings are
        /// ID3's own numbering, and a mislabelled frame is common enough that a
        /// failed decode returns empty rather than throwing.
        /// </summary>
        static string DecodeTextFrame(byte[] body, int at, int size, bool unsync)
        {
            if (size < 1) return "";
            byte encoding = body[at];
            int from = at + 1, len = size - 1;
            if (len <= 0) return "";

            byte[] data;
            if (unsync)
            {
                // Unsynchronisation inserts a zero after every 0xFF so no byte run
                // can be mistaken for an MPEG frame sync; undo it before decoding.
                var tmp = new byte[len];
                int n = 0;
                for (int i = 0; i < len; i++)
                {
                    tmp[n++] = body[from + i];
                    if (body[from + i] == 0xFF && i + 1 < len && body[from + i + 1] == 0x00) i++;
                }
                data = new byte[n];
                Array.Copy(tmp, data, n);
            }
            else
            {
                data = new byte[len];
                Array.Copy(body, from, data, 0, len);
            }

            try
            {
                switch (encoding)
                {
                    case 0: return Trim(Latin1().GetString(data));
                    case 1: return Trim(Utf16WithBom(data));
                    case 2: return Trim(Encoding.BigEndianUnicode.GetString(data));
                    case 3: return Trim(new UTF8Encoding(false).GetString(data));
                    default: return Trim(Latin1().GetString(data));
                }
            }
            catch (ArgumentException) { return ""; }
        }

        static string Utf16WithBom(byte[] data)
        {
            if (data.Length >= 2)
            {
                if (data[0] == 0xFF && data[1] == 0xFE)
                    return Encoding.Unicode.GetString(data, 2, data.Length - 2);
                if (data[0] == 0xFE && data[1] == 0xFF)
                    return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
            }
            // No BOM: the spec requires one, but plenty of taggers omit it and
            // little-endian is what they all mean.
            return Encoding.Unicode.GetString(data);
        }

        // A frame is terminated by a null and may be padded with more; a
        // multi-value frame separates with them too, and only the first is shown.
        static string Trim(string s)
        {
            if (s == null) return "";
            int nul = s.IndexOf('\0');
            if (nul >= 0) s = s.Substring(0, nul);
            return s.Trim();
        }

        static Encoding Latin1() { return Encoding.GetEncoding(28591); }

        /// <summary>ID3's sync-safe integer: seven bits per byte, top bit always
        /// clear so the length can never look like a frame sync.</summary>
        static int SyncSafe(byte[] b, int at)
        {
            if (at + 3 >= b.Length) return 0;
            return ((b[at] & 0x7F) << 21) | ((b[at + 1] & 0x7F) << 14)
                 | ((b[at + 2] & 0x7F) << 7) | (b[at + 3] & 0x7F);
        }

        static int ReadInt32(byte[] b, int at)
        {
            if (at + 3 >= b.Length) return 0;
            return (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
        }

        // ---- ID3v1 -----------------------------------------------------------

        static void ReadId3v1(Stream s, TrackTags tags)
        {
            if (s.Length < 128) return;
            var b = new byte[128];
            s.Position = s.Length - 128;
            if (Fill(s, b, 0, 128) < 128) return;
            if (b[0] != 'T' || b[1] != 'A' || b[2] != 'G') return;

            Prefer(ref tags.Title, Field(b, 3, 30));
            Prefer(ref tags.Artist, Field(b, 33, 30));
            Prefer(ref tags.Album, Field(b, 63, 30));
            Prefer(ref tags.Year, Year(Field(b, 93, 4)));
        }

        // ID3v1 fields are null- or space-padded, and nominally Latin-1 — there is
        // no encoding byte, so anything else in there is unrecoverable by design.
        static string Field(byte[] b, int at, int len)
        {
            int end = at + len;
            while (end > at && (b[end - 1] == 0 || b[end - 1] == 0x20)) end--;
            if (end <= at) return "";
            try { return Latin1().GetString(b, at, end - at).Trim(); }
            catch (ArgumentException) { return ""; }
        }

        // ---- FLAC ------------------------------------------------------------

        static void ReadFlac(Stream s, TrackTags tags)
        {
            s.Position = 4;
            var head = new byte[4];
            for (int block = 0; block < 64; block++)
            {
                if (Fill(s, head, 0, 4) < 4) return;
                bool last = (head[0] & 0x80) != 0;
                int type = head[0] & 0x7F;
                int len = (head[1] << 16) | (head[2] << 8) | head[3];
                if (len < 0) return;

                if (type == 4)   // VORBIS_COMMENT
                {
                    if (len > MaxHeader) return;
                    var body = new byte[len];
                    if (Fill(s, body, 0, len) < len) return;
                    ReadVorbisComments(body, 0, len, tags);
                    return;
                }
                if (last) return;
                s.Position += len;
                if (s.Position >= s.Length) return;
            }
        }

        // ---- Ogg (Vorbis / Opus) --------------------------------------------

        /// <summary>
        /// The comment header of an Ogg stream. Only the first few pages are
        /// searched: the comment header is required to be right after the identity
        /// header, so if it is not near the front it is not there.
        /// </summary>
        static void ReadOgg(Stream s, TrackTags tags)
        {
            int scan = (int)Math.Min(s.Length, 64 * 1024);
            var buf = new byte[scan];
            s.Position = 0;
            int have = Fill(s, buf, 0, scan);

            for (int i = 0; i + 8 < have; i++)
            {
                // Vorbis: packet type 3 then "vorbis". Opus: "OpusTags".
                if (buf[i] == 3 && Match(buf, i + 1, "vorbis"))
                {
                    ReadVorbisComments(buf, i + 7, have - (i + 7), tags);
                    return;
                }
                if (Match(buf, i, "OpusTags"))
                {
                    ReadVorbisComments(buf, i + 8, have - (i + 8), tags);
                    return;
                }
            }
        }

        static bool Match(byte[] b, int at, string ascii)
        {
            if (at < 0 || at + ascii.Length > b.Length) return false;
            for (int i = 0; i < ascii.Length; i++) if (b[at + i] != (byte)ascii[i]) return false;
            return true;
        }

        /// <summary>
        /// A Vorbis comment block: a vendor string, a count, then that many
        /// length-prefixed UTF-8 `FIELD=value` entries. Every length is checked
        /// against what is left before it is used.
        /// </summary>
        static void ReadVorbisComments(byte[] b, int at, int len, TrackTags tags)
        {
            int end = at + len;
            if (at + 4 > end) return;
            int vendor = LittleInt(b, at);
            at += 4;
            if (vendor < 0 || at + vendor > end) return;
            at += vendor;
            if (at + 4 > end) return;
            int count = LittleInt(b, at);
            at += 4;
            if (count < 0 || count > 4096) return;

            var utf8 = new UTF8Encoding(false);
            for (int i = 0; i < count; i++)
            {
                if (at + 4 > end) return;
                int size = LittleInt(b, at);
                at += 4;
                if (size < 0 || at + size > end) return;
                string entry;
                try { entry = utf8.GetString(b, at, size); }
                catch (ArgumentException) { return; }
                at += size;

                int eq = entry.IndexOf('=');
                if (eq <= 0) continue;
                string key = entry.Substring(0, eq).ToUpperInvariant();
                string value = entry.Substring(eq + 1).Trim();
                if (value.Length == 0) continue;

                switch (key)
                {
                    case "ARTIST": case "ALBUMARTIST": Prefer(ref tags.Artist, value); break;
                    case "TITLE": Prefer(ref tags.Title, value); break;
                    case "ALBUM": Prefer(ref tags.Album, value); break;
                    case "DATE": case "YEAR": Prefer(ref tags.Year, Year(value)); break;
                }
            }
        }

        static int LittleInt(byte[] b, int at)
        {
            if (at + 3 >= b.Length) return -1;
            // Read into a long first: a size with the top bit set is corrupt, and
            // shifting it into an int would make it negative in a way that looks
            // like a valid check failure rather than the overflow it is.
            long v = (long)b[at] | ((long)b[at + 1] << 8) | ((long)b[at + 2] << 16)
                   | ((long)b[at + 3] << 24);
            return v > int.MaxValue ? -1 : (int)v;
        }

        // ---- MP4 / M4A -------------------------------------------------------

        /// <summary>
        /// iTunes-style metadata: moov → udta → meta → ilst, with each tag an atom
        /// whose name is a four-character code and whose value is in a nested
        /// `data` atom.
        /// </summary>
        static void ReadMp4(Stream s, TrackTags tags)
        {
            long ilstAt, ilstLen;
            if (!FindAtom(s, 0, s.Length, "moov", out ilstAt, out ilstLen)) return;
            long udtaAt, udtaLen;
            if (!FindAtom(s, ilstAt, ilstLen, "udta", out udtaAt, out udtaLen)) return;
            long metaAt, metaLen;
            if (!FindAtom(s, udtaAt, udtaLen, "meta", out metaAt, out metaLen)) return;
            // `meta` is a full atom: four bytes of version and flags before its
            // children start. Skipping them is what the next search depends on.
            metaAt += 4;
            metaLen -= 4;
            if (metaLen <= 0) return;
            long listAt, listLen;
            if (!FindAtom(s, metaAt, metaLen, "ilst", out listAt, out listLen)) return;
            if (listLen > MaxHeader) listLen = MaxHeader;

            var body = new byte[listLen];
            s.Position = listAt;
            int have = Fill(s, body, 0, (int)listLen);

            int p = 0;
            while (p + 8 <= have)
            {
                long size = ReadUInt32(body, p);
                if (size < 8 || p + size > have) break;
                // Latin-1, not ASCII: the iTunes atom names start with 0xA9, and
                // an ASCII decode turns that byte into '?' — after which none of
                // the four comparisons below can ever match.
                string name = Latin1().GetString(body, p + 4, 4);
                string value = Mp4Value(body, p + 8, (int)size - 8);
                if (!string.IsNullOrEmpty(value))
                {
                    // The leading byte of the iTunes codes is 0xA9, which lands in
                    // a Latin-1 decode as '©'.
                    if (name == "©ART" || name == "aART") Prefer(ref tags.Artist, value);
                    else if (name == "©nam") Prefer(ref tags.Title, value);
                    else if (name == "©alb") Prefer(ref tags.Album, value);
                    else if (name == "©day") Prefer(ref tags.Year, Year(value));
                }
                p += (int)size;
            }
        }

        /// <summary>The `data` atom inside a tag: eight bytes of type and locale,
        /// then the payload — UTF-8 when the type code says 1.</summary>
        static string Mp4Value(byte[] b, int at, int len)
        {
            int end = at + len;
            while (at + 8 <= end)
            {
                long size = ReadUInt32(b, at);
                if (size < 8 || at + size > end) return "";
                if (Encoding.ASCII.GetString(b, at + 4, 4) == "data" && size >= 16)
                {
                    int type = (int)(ReadUInt32(b, at + 8) & 0xFFFFFF);
                    int from = at + 16, count = (int)size - 16;
                    if (count <= 0) return "";
                    if (type != 1) return "";   // not text: a byte count, a genre index
                    try { return new UTF8Encoding(false).GetString(b, from, count).Trim(); }
                    catch (ArgumentException) { return ""; }
                }
                at += (int)size;
            }
            return "";
        }

        /// <summary>
        /// Finds a child atom by name inside a container. Sizes are read as
        /// unsigned into a long: a 32-bit size read as signed goes negative on any
        /// file over 2 GB and walks the parser off the end.
        /// </summary>
        static bool FindAtom(Stream s, long from, long length, string name,
            out long bodyAt, out long bodyLen)
        {
            bodyAt = 0;
            bodyLen = 0;
            long at = from, end = from + length;
            if (end > s.Length) end = s.Length;
            var head = new byte[8];

            while (at + 8 <= end)
            {
                s.Position = at;
                if (Fill(s, head, 0, 8) < 8) return false;
                long size = ReadUInt32(head, 0);
                string atom = Encoding.ASCII.GetString(head, 4, 4);
                int headerLen = 8;

                if (size == 1)
                {
                    // A 64-bit size follows the header for atoms over 4 GB.
                    var big = new byte[8];
                    if (Fill(s, big, 0, 8) < 8) return false;
                    size = 0;
                    for (int i = 0; i < 8; i++) size = (size << 8) | big[i];
                    headerLen = 16;
                }
                else if (size == 0)
                {
                    size = end - at;   // "to the end of the file"
                }

                if (size < headerLen || at + size > end) return false;
                if (atom == name)
                {
                    bodyAt = at + headerLen;
                    bodyLen = size - headerLen;
                    return bodyLen > 0;
                }
                at += size;
            }
            return false;
        }

        static long ReadUInt32(byte[] b, int at)
        {
            if (at + 3 >= b.Length) return 0;
            return ((long)b[at] << 24) | ((long)b[at + 1] << 16)
                 | ((long)b[at + 2] << 8) | b[at + 3];
        }

        // ---- How long it runs -------------------------------------------------
        //
        // Every container here states its own length, so the tag pass can answer
        // "how long is this track" without opening a decoder. It matters because
        // the decoder only ever runs on the track being played: read the length
        // from it alone and every row nobody has played yet shows --:--, and the
        // playlist total stays wrong until the whole list has been through the
        // speakers once.
        //
        // The same rule as the text readers applies: every length comes out of
        // the file, is checked against what is really there, and an unreadable
        // header leaves the duration at zero rather than throwing. Zero is a
        // legitimate answer — Matroska and raw AAC are not parsed here, and the
        // decoder still fills those in when the track starts.

        /// <summary>
        /// An mp3. Nothing in the file states the length, so it has to be
        /// derived: a Xing/Info or VBRI header in the first frame carries the
        /// frame count outright, which is the only honest answer for a variable
        /// bitrate file, and without one the length is the audio byte count over
        /// the constant bitrate.
        /// </summary>
        static void MpegLength(Stream s, TrackTags tags)
        {
            long start = AudioStart(s);
            long end = AudioEnd(s);
            if (end - start < 4) return;

            // Junk in front of the first sync is common, and a byte pair inside
            // it can look like one — which is why a candidate has to parse as a
            // header and be followed by another frame where it says the next one
            // begins.
            int scan = (int)Math.Min(end - start, 64 * 1024);
            var buf = new byte[scan];
            s.Position = start;
            int have = Fill(s, buf, 0, scan);

            for (int i = 0; i + 4 <= have; i++)
            {
                if (buf[i] != 0xFF || (buf[i + 1] & 0xE0) != 0xE0) continue;
                MpegFrame f = MpegFrame.Parse(buf, i);
                if (f == null) continue;
                int next = i + f.Length;
                if (next + 4 <= have && MpegFrame.Parse(buf, next) == null) continue;

                long frames = VbrFrames(buf, i, f, have);
                if (frames > 0)
                {
                    tags.Duration = frames * (double)f.Samples / f.Rate;
                    return;
                }
                tags.Duration = (end - (start + i)) * 8.0 / (f.Bitrate * 1000.0);
                return;
            }
        }

        /// <summary>Where the audio begins: past an ID3v2 tag if there is one.
        /// Counting the tag as audio adds a second for every 16 KB of cover
        /// art.</summary>
        static long AudioStart(Stream s)
        {
            var head = new byte[10];
            s.Position = 0;
            if (Fill(s, head, 0, 10) < 10) return 0;
            if (head[0] != 'I' || head[1] != 'D' || head[2] != '3') return 0;
            int size = SyncSafe(head, 6);
            if (size <= 0) return 0;
            long at = 10L + size;
            if ((head[5] & 0x10) != 0) at += 10;   // a v4 footer
            return at > 0 && at < s.Length ? at : 0;
        }

        /// <summary>Where it stops: before an ID3v1 block, which is 128 bytes of
        /// text that would otherwise be counted as music.</summary>
        static long AudioEnd(Stream s)
        {
            long end = s.Length;
            if (end < 128) return end;
            var b = new byte[3];
            s.Position = end - 128;
            if (Fill(s, b, 0, 3) == 3 && b[0] == 'T' && b[1] == 'A' && b[2] == 'G') end -= 128;
            return end;
        }

        /// <summary>The frame count out of a Xing, Info or VBRI header, or zero
        /// when the first frame carries none of them.</summary>
        static long VbrFrames(byte[] b, int at, MpegFrame f, int have)
        {
            // Xing sits where the side information ends, which depends on the
            // version and the channel mode; VBRI is always 32 bytes past the
            // header whatever the frame is.
            int side = f.Version1 ? (f.Mono ? 17 : 32) : (f.Mono ? 9 : 17);
            int p = at + 4 + side;
            if (p + 12 <= have && (Match(b, p, "Xing") || Match(b, p, "Info")))
            {
                long flags = ReadUInt32(b, p + 4);
                return (flags & 1) != 0 ? ReadUInt32(b, p + 8) : 0;
            }
            int v = at + 36;
            if (v + 26 <= have && Match(b, v, "VBRI")) return ReadUInt32(b, v + 14);
            return 0;
        }

        /// <summary>One MPEG audio frame header, as far as the length needs
        /// it.</summary>
        sealed class MpegFrame
        {
            public int Rate;      // Hz
            public int Bitrate;   // kbps
            public int Samples;   // per frame
            public int Length;    // bytes, this frame including its header
            public bool Mono;
            public bool Version1; // MPEG 1, as opposed to MPEG 2 or 2.5

            // Indexed [version and layer][bitrate index], in kbps. Index 0 is the
            // free format and 15 is reserved; both are refused rather than
            // guessed at.
            static readonly int[][] Bitrates =
            {
                new[] { 0,32,64,96,128,160,192,224,256,288,320,352,384,416,448,0 }, // v1 layer I
                new[] { 0,32,48,56, 64, 80, 96,112,128,160,192,224,256,320,384,0 }, // v1 layer II
                new[] { 0,32,40,48, 56, 64, 80, 96,112,128,160,192,224,256,320,0 }, // v1 layer III
                new[] { 0,32,48,56, 64, 80, 96,112,128,144,160,176,192,224,256,0 }, // v2 layer I
                new[] { 0, 8,16,24, 32, 40, 48, 56, 64, 80, 96,112,128,144,160,0 }  // v2 layer II/III
            };

            static readonly int[][] Rates =
            {
                new[] { 44100, 48000, 32000 },   // MPEG 1
                new[] { 22050, 24000, 16000 },   // MPEG 2
                new[] { 11025, 12000,  8000 }    // MPEG 2.5
            };

            /// <summary>Null for anything that is not a real frame header — a
            /// reserved version or layer, the free format, or a reserved sample
            /// rate. Most false syncs fail one of those.</summary>
            public static MpegFrame Parse(byte[] b, int at)
            {
                if (at < 0 || at + 4 > b.Length) return null;
                if (b[at] != 0xFF || (b[at + 1] & 0xE0) != 0xE0) return null;

                int version = (b[at + 1] >> 3) & 3;   // 0 = 2.5, 1 = reserved, 2 = 2, 3 = 1
                int layerBits = (b[at + 1] >> 1) & 3; // 1 = III, 2 = II, 3 = I
                if (version == 1 || layerBits == 0) return null;
                int bitrateIndex = (b[at + 2] >> 4) & 0xF;
                int rateIndex = (b[at + 2] >> 2) & 3;
                if (bitrateIndex == 0 || bitrateIndex == 15 || rateIndex == 3) return null;

                bool v1 = version == 3;
                int layer = layerBits == 3 ? 1 : (layerBits == 2 ? 2 : 3);
                int row = v1 ? layer - 1 : (layer == 1 ? 3 : 4);
                int bitrate = Bitrates[row][bitrateIndex];
                int rate = Rates[version == 3 ? 0 : (version == 2 ? 1 : 2)][rateIndex];
                if (bitrate <= 0 || rate <= 0) return null;

                var f = new MpegFrame();
                f.Rate = rate;
                f.Bitrate = bitrate;
                f.Version1 = v1;
                f.Mono = ((b[at + 3] >> 6) & 3) == 3;
                // Layer III at MPEG 2 or 2.5 halves the frame; everything else
                // keeps the count its layer was defined at.
                f.Samples = layer == 1 ? 384 : (layer == 3 && !v1 ? 576 : 1152);
                int padding = (b[at + 2] >> 1) & 1;
                f.Length = layer == 1
                    ? (12 * bitrate * 1000 / rate + padding) * 4
                    : f.Samples / 8 * bitrate * 1000 / rate + padding;
                return f.Length > 4 ? f : null;
            }
        }

        /// <summary>
        /// FLAC states the sample count outright. STREAMINFO is required to be
        /// the first metadata block, so its position is fixed: the four magic
        /// bytes, a four-byte block header, then 34 bytes of it.
        /// </summary>
        static void FlacLength(Stream s, TrackTags tags)
        {
            var b = new byte[38];
            s.Position = 4;
            if (Fill(s, b, 0, 38) < 38) return;
            if ((b[0] & 0x7F) != 0) return;   // not STREAMINFO after all
            int rate = (b[14] << 12) | (b[15] << 4) | (b[16] >> 4);
            long total = ((long)(b[17] & 0x0F) << 32) | ((long)b[18] << 24)
                       | ((long)b[19] << 16) | ((long)b[20] << 8) | b[21];
            // A zero sample count is the format's own "unknown", written by an
            // encoder streaming to a pipe, and not something to divide by.
            if (rate > 0 && total > 0) tags.Duration = total / (double)rate;
        }

        /// <summary>
        /// An Ogg stream carries no duration field: the length is the granule
        /// position on its last page over the sample rate from its identification
        /// header, so both ends of the file have to be read.
        /// </summary>
        static void OggLength(Stream s, TrackTags tags)
        {
            int scan = (int)Math.Min(s.Length, 64 * 1024);
            var front = new byte[scan];
            s.Position = 0;
            int have = Fill(s, front, 0, scan);

            double rate = 0;
            long skip = 0;
            for (int i = 0; i + 16 <= have; i++)
            {
                if (front[i] == 1 && Match(front, i + 1, "vorbis"))
                {
                    rate = LittleUInt(front, i + 12);
                    break;
                }
                if (Match(front, i, "OpusHead"))
                {
                    // Opus counts granules at 48 kHz whatever the source rate
                    // was, and the pre-skip in front of them is padding, not
                    // music.
                    rate = 48000;
                    skip = front[i + 10] | ((long)front[i + 11] << 8);
                    break;
                }
            }
            if (rate <= 0) return;

            long granule = LastGranule(s);
            if (granule > skip) tags.Duration = (granule - skip) / rate;
        }

        /// <summary>The granule position of the last page that finishes a packet.
        /// A page that finishes none states all ones, which reads as negative
        /// here and is stepped over rather than believed.</summary>
        static long LastGranule(Stream s)
        {
            int scan = (int)Math.Min(s.Length, 128 * 1024);
            var tail = new byte[scan];
            s.Position = s.Length - scan;
            int have = Fill(s, tail, 0, scan);
            for (int i = have - 27; i >= 0; i--)
            {
                if (tail[i] != 'O' || !Match(tail, i, "OggS")) continue;
                long g = LittleLong(tail, i + 6);
                if (g > 0) return g;
            }
            return 0;
        }

        /// <summary>MP4 states the length in the movie header: a duration in
        /// whatever units the timescale beside it names.</summary>
        static void Mp4Length(Stream s, TrackTags tags)
        {
            long moovAt, moovLen;
            if (!FindAtom(s, 0, s.Length, "moov", out moovAt, out moovLen)) return;
            long at, len;
            if (!FindAtom(s, moovAt, moovLen, "mvhd", out at, out len)) return;

            var b = new byte[32];
            s.Position = at;
            int want = (int)Math.Min(32, len);
            int have = Fill(s, b, 0, want);
            if (have < 20) return;

            long timescale, duration;
            if (b[0] == 1)
            {
                // Version 1 widens both timestamps and the duration to 64 bits.
                if (have < 32) return;
                timescale = ReadUInt32(b, 20);
                duration = LongBe(b, 24);
            }
            else
            {
                timescale = ReadUInt32(b, 12);
                duration = ReadUInt32(b, 16);
                if (duration == 0xFFFFFFFFL) return;   // the format's "unknown"
            }
            if (timescale > 0 && duration > 0) tags.Duration = duration / (double)timescale;
        }

        /// <summary>A RIFF wave: the byte rate is in the format chunk and the
        /// length of the audio is the size of the data chunk.</summary>
        static void WavLength(Stream s, TrackTags tags)
        {
            long at = 12, end = s.Length;
            long byteRate = 0;
            var head = new byte[8];
            var fmt = new byte[16];
            while (at + 8 <= end)
            {
                s.Position = at;
                if (Fill(s, head, 0, 8) < 8) return;
                long size = LittleUInt(head, 4);
                if (size < 0) return;

                if (Match(head, 0, "fmt ") && size >= 16)
                {
                    if (Fill(s, fmt, 0, 16) < 16) return;
                    byteRate = LittleUInt(fmt, 8);
                }
                else if (Match(head, 0, "data"))
                {
                    // A wav written to a pipe carries a placeholder size; what is
                    // actually in the file is the honest answer.
                    long real = end - (at + 8);
                    if (size <= 0 || size > real) size = real;
                    if (byteRate > 0 && size > 0) tags.Duration = size / (double)byteRate;
                    return;
                }
                at += 8 + size + (size & 1);   // chunks are padded to even lengths
            }
        }

        // ASF_File_Properties_Object as it is laid out on disc: the first three
        // fields of a GUID are little-endian and the last eight bytes are not.
        static readonly byte[] AsfFileProperties =
        {
            0xA1, 0xDC, 0xAB, 0x8C, 0x47, 0xA9, 0xCF, 0x11,
            0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65
        };

        /// <summary>A wma: the file properties object states the play duration in
        /// hundreds of nanoseconds, with the preroll it includes stated beside
        /// it.</summary>
        static void AsfLength(Stream s, TrackTags tags)
        {
            int scan = (int)Math.Min(s.Length, 64 * 1024);
            var buf = new byte[scan];
            s.Position = 0;
            int have = Fill(s, buf, 0, scan);

            for (int i = 0; i + 88 <= have; i++)
            {
                if (buf[i] != AsfFileProperties[0]) continue;
                bool same = true;
                for (int k = 1; k < AsfFileProperties.Length && same; k++)
                    same = buf[i + k] == AsfFileProperties[k];
                if (!same) continue;

                // The preroll is how much the player is meant to buffer before it
                // starts; it is counted into the play duration and is not music.
                long play = LittleLong(buf, i + 64);
                long preroll = LittleLong(buf, i + 80);
                if (play <= 0) return;
                double seconds = (play / 10000.0 - preroll) / 1000.0;
                if (seconds > 0) tags.Duration = seconds;
                return;
            }
        }

        /// <summary>An unsigned 32-bit little-endian field, widened rather than
        /// wrapped: a size with the top bit set is corrupt, not negative.</summary>
        static long LittleUInt(byte[] b, int at)
        {
            if (at < 0 || at + 3 >= b.Length) return -1;
            return (long)b[at] | ((long)b[at + 1] << 8)
                 | ((long)b[at + 2] << 16) | ((long)b[at + 3] << 24);
        }

        /// <summary>An unsigned 64-bit little-endian field. Anything with the top
        /// bit set comes back negative, which every caller reads as "not a usable
        /// value" — and for a granule position that is exactly what it means.</summary>
        static long LittleLong(byte[] b, int at)
        {
            if (at < 0 || at + 7 >= b.Length) return -1;
            long v = 0;
            for (int i = 7; i >= 0; i--) v = (v << 8) | b[at + i];
            return v;
        }

        static long LongBe(byte[] b, int at)
        {
            if (at < 0 || at + 7 >= b.Length) return 0;
            long v = 0;
            for (int i = 0; i < 8; i++) v = (v << 8) | b[at + i];
            return v < 0 ? 0 : v;
        }
    }
}
