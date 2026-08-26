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
                ReadId3v2(s, tags);
            else if (magic[0] == 'f' && magic[1] == 'L' && magic[2] == 'a' && magic[3] == 'C')
                ReadFlac(s, tags);
            else if (magic[0] == 'O' && magic[1] == 'g' && magic[2] == 'g' && magic[3] == 'S')
                ReadOgg(s, tags);
            else if (magic[4] == 'f' && magic[5] == 't' && magic[6] == 'y' && magic[7] == 'p')
                ReadMp4(s, tags);

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
    }
}
