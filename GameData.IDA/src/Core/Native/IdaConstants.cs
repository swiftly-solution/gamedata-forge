namespace GameData.IDA.Core.Native;

/// <summary>
/// The constants the managed face needs, transcribed from the SDK headers.
/// </summary>
/// <remarks>
/// <para>
/// The generator emits functions only. IDA's flag values live in <c>#define</c>s and unscoped
/// enums whose bodies are arithmetic over other macros, so they are transcribed by hand as they
/// are needed rather than half-parsed. Each group names its header so it can be checked.
/// </para>
/// <para>
/// Everything here is a bit flag, which the SDK does not renumber between releases, so these are
/// the same for every supported version. Values that <em>are</em> version-dependent live in
/// <see cref="IdaAbi"/> instead.
/// </para>
/// </remarks>
public static class IdaConstants
{
    /// <summary>The address IDA uses for "no address". <c>ea_t(-1)</c>, from pro.h.</summary>
    public const ulong BadAddress = ulong.MaxValue;

    /// <summary>Flags for <c>get_ea_name</c> — GN_, name.hpp.</summary>
    public static class GetName
    {
        public const int Visible = 0x0001;
        public const int Colored = 0x0002;
        public const int Demangled = 0x0004;
        public const int Strict = 0x0008;
        public const int Short = 0x0010;
        public const int Long = 0x0020;
        public const int Local = 0x0040;
        public const int NotDummy = 0x0200;
    }

    /// <summary>Flags for <c>set_name</c> — SN_, name.hpp.</summary>
    public static class SetName
    {
        public const int Check = 0x00;
        public const int NoCheck = 0x01;
        public const int Public = 0x02;
        public const int NonPublic = 0x04;
        public const int Auto = 0x20;
        public const int NonAuto = 0x40;
    }

    /// <summary>Flags for <c>bin_search</c> — BIN_SEARCH_, bytes.hpp.</summary>
    public static class BinSearch
    {
        public const int NoCase = 0x00;
        public const int Case = 0x01;
        public const int NoBreak = 0x02;
        public const int Inited = 0x04;
        public const int NoShow = 0x08;
        public const int Forward = 0x00;
        public const int Backward = 0x10;
        public const int Bitmask = 0x20;
    }

    /// <summary>Flags for <c>get_bytes</c> — GMB_, bytes.hpp.</summary>
    public static class GetBytes
    {
        public const int ReadAll = 0x01;
        public const int WaitBox = 0x02;
    }
}
