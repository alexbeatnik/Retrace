// Media Foundation, declared by hand.
//
// MF is how this player decodes anything at all: the Source Reader wraps the
// same demuxers and codecs the rest of Windows uses, so mp3, wav, flac, m4a,
// aac, wma and mp4 all arrive as PCM without a single byte of third-party
// code. The interfaces are written out here rather than pulled from an interop
// assembly, because an interop assembly would be a dependency and this project
// has none.
//
// The one rule that matters below: a COM interface's vtable is its declaration
// order, and C# does not inherit vtables across [ComImport] interfaces. Every
// interface that derives from IMFAttributes therefore repeats all thirty of its
// slots, in order, before adding its own. Insert a method in the wrong place and
// the calls do not fail — they land on a different function.
using System;
using System.Runtime.InteropServices;

namespace Retrace
{
    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFAttributes
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr val);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr val, out bool result);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out bool result);
        [PreserveSig] int GetUINT32(ref Guid key, out int value);
        [PreserveSig] int GetUINT64(ref Guid key, out long value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out int length);
        [PreserveSig] int GetString(ref Guid key, IntPtr buf, int size, IntPtr len);
        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr buf, out int len);
        [PreserveSig] int GetBlobSize(ref Guid key, out int size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buf, int size, IntPtr fill);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buf, out int size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid key, IntPtr val);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, int value);
        [PreserveSig] int SetUINT64(ref Guid key, long value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buf, int size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr unk);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr val);
        [PreserveSig] int CopyAllItems(IMFAttributes dest);
    }

    // IMFMediaType : IMFAttributes — the thirty slots above, then five of its own.
    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaType
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr val);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr val, out bool result);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out bool result);
        [PreserveSig] int GetUINT32(ref Guid key, out int value);
        [PreserveSig] int GetUINT64(ref Guid key, out long value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out int length);
        [PreserveSig] int GetString(ref Guid key, IntPtr buf, int size, IntPtr len);
        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr buf, out int len);
        [PreserveSig] int GetBlobSize(ref Guid key, out int size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buf, int size, IntPtr fill);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buf, out int size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid key, IntPtr val);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, int value);
        [PreserveSig] int SetUINT64(ref Guid key, long value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buf, int size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr unk);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr val);
        [PreserveSig] int CopyAllItems(IMFAttributes dest);
        [PreserveSig] int GetMajorType(out Guid major);
        [PreserveSig] int IsCompressedFormat(out bool compressed);
        [PreserveSig] int IsEqual(IMFMediaType other, out int flags);
        [PreserveSig] int GetRepresentation(Guid rep, out IntPtr data);
        [PreserveSig] int FreeRepresentation(Guid rep, IntPtr data);
    }

    [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buf, out int maxLen, out int curLen);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out int len);
        [PreserveSig] int SetCurrentLength(int len);
        [PreserveSig] int GetMaxLength(out int len);
    }

    // IMFSample : IMFAttributes — same thirty, then fourteen of its own.
    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSample
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr val);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr val, out bool result);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out bool result);
        [PreserveSig] int GetUINT32(ref Guid key, out int value);
        [PreserveSig] int GetUINT64(ref Guid key, out long value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out int length);
        [PreserveSig] int GetString(ref Guid key, IntPtr buf, int size, IntPtr len);
        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr buf, out int len);
        [PreserveSig] int GetBlobSize(ref Guid key, out int size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buf, int size, IntPtr fill);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buf, out int size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid key, IntPtr val);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, int value);
        [PreserveSig] int SetUINT64(ref Guid key, long value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buf, int size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr unk);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr val);
        [PreserveSig] int CopyAllItems(IMFAttributes dest);
        [PreserveSig] int GetSampleFlags(out int flags);
        [PreserveSig] int SetSampleFlags(int flags);
        [PreserveSig] int GetSampleTime(out long time);
        [PreserveSig] int SetSampleTime(long time);
        [PreserveSig] int GetSampleDuration(out long dur);
        [PreserveSig] int SetSampleDuration(long dur);
        [PreserveSig] int GetBufferCount(out int count);
        [PreserveSig] int GetBufferByIndex(int index, out IMFMediaBuffer buf);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buf);
        [PreserveSig] int AddBuffer(IMFMediaBuffer buf);
        [PreserveSig] int RemoveBufferByIndex(int index);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out int len);
        [PreserveSig] int CopyToBuffer(IMFMediaBuffer buf);
    }

    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(int index, out bool selected);
        [PreserveSig] int SetStreamSelection(int index, bool selected);
        [PreserveSig] int GetNativeMediaType(int index, int typeIndex, out IMFMediaType type);
        [PreserveSig] int GetCurrentMediaType(int index, out IMFMediaType type);
        [PreserveSig] int SetCurrentMediaType(int index, IntPtr reserved, IMFMediaType type);
        [PreserveSig] int SetCurrentPosition(ref Guid format, ref PropVariant pos);
        [PreserveSig] int ReadSample(int index, int flags, out int actualIndex,
            out int streamFlags, out long timestamp, out IMFSample sample);
        [PreserveSig] int Flush(int index);
        [PreserveSig] int GetServiceForStream(int index, ref Guid service, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetPresentationAttribute(int index, ref Guid key, out PropVariant value);
    }

    /// <summary>
    /// PROPVARIANT, cut down to the one member this player reads. The tag sits in
    /// the first two bytes and the union starts at offset 8 on both architectures
    /// (the three reserved words pad it there); a 64-bit field at that offset
    /// covers the VT_I8 durations and the VT_UI8 seek positions alike. The struct
    /// is 24 bytes on x64 — the size MF writes — so the two IntPtr-sized slots at
    /// the end must stay even though nothing here touches them.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public long longValue;
        [FieldOffset(8)] public IntPtr pointerValue;
        [FieldOffset(16)] public IntPtr tail;

        public const ushort VT_I8 = 20;
        public const ushort VT_UI8 = 21;
    }

    static class Mf
    {
        // MF_VERSION for Windows 7 and later: (0x0002 << 16) | 0x0070.
        public const int Version = 0x00020070;
        public const int MFSTARTUP_LITE = 1;

        // Negative stream selectors, as unsigned constants narrowed to int.
        public const int FirstAudioStream = unchecked((int)0xFFFFFFFD);
        public const int AllStreams = unchecked((int)0xFFFFFFFE);
        public const int MediaSource = unchecked((int)0xFFFFFFFF);

        // MF_SOURCE_READERF_* flags on ReadSample's streamFlags.
        public const int EndOfStream = 0x2;
        public const int CurrentMediaTypeChanged = 0x10;

        public const int MF_E_INVALIDMEDIATYPE = unchecked((int)0xC00D36B4);

        public static Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static Guid MFMediaType_Audio = new Guid("73647561-0000-0010-8000-00aa00389b71");
        public static Guid MFAudioFormat_Float = new Guid("00000003-0000-0010-8000-00aa00389b71");
        public static Guid MF_MT_AUDIO_NUM_CHANNELS = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        public static Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        public static Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
        public static Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");
        public static Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731");
        public static Guid MF_PD_DURATION = new Guid("6c990d33-bb8e-477a-8598-0d5d96fcd88a");
        public static Guid MF_PD_AUDIO_ENCODING_BITRATE = new Guid("a20ac996-6bb5-4cd2-a6f9-1e6dcd5b8d78");
        // Passing GUID_NULL as the time format means "100-nanosecond units".
        public static Guid TimeFormatNone = Guid.Empty;

        [DllImport("mfplat.dll")] public static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll")] public static extern int MFShutdown();
        [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType type);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
        public static extern int MFCreateSourceReaderFromURL(string url, IMFAttributes attrs,
            out IMFSourceReader reader);
        [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PropVariant pv);

        static bool started;
        static readonly object startLock = new object();

        /// <summary>
        /// Brings the platform up once per process. Every entry point that can
        /// reach a decoder calls this — the tag reader runs before anything has
        /// been played, and the metadata scan runs on a pool thread, so there is
        /// no single moment early enough to do it in.
        /// </summary>
        public static bool Startup()
        {
            lock (startLock)
            {
                if (started) return true;
                started = MFStartup(Version, MFSTARTUP_LITE) >= 0;
                return started;
            }
        }

        /// <summary>Releases a COM object obtained through this interop, ignoring
        /// the double-release that a failed construction path can produce.</summary>
        public static void Release(object com)
        {
            if (com == null) return;
            try { Marshal.ReleaseComObject(com); }
            catch (ArgumentException) { }
        }
    }
}
