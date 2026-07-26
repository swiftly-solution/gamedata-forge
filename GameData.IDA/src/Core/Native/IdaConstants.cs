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
        public const int NoWarn = 0x100;

        /// <summary>Rename whatever already holds the name rather than refusing.</summary>
        public const int Force = 0x800;
    }

    /// <summary>Function flags — FUNC_, funcs.hpp. These live in <c>func_t::flags</c>.</summary>
    public static class FuncFlags
    {
        public const ulong NoRet = 0x00000001;
        public const ulong Lib = 0x00000004;

        /// <summary>A jump function: the body is one jump to the real implementation.</summary>
        public const ulong Thunk = 0x00000080;

        /// <summary>
        /// Set by <c>func_t</c>'s constructor: the non-return analysis has not run yet.
        /// </summary>
        public const ulong NoRetPending = 0x00000200;

        public const ulong Tail = 0x00008000;
    }

    /// <summary>Code cross-reference types — <c>cref_t</c>, xref.hpp.</summary>
    public static class Cref
    {
        public const int CallFar = 16;
        public const int CallNear = 17;
        public const int JumpFar = 18;
        public const int JumpNear = 19;
        public const int Flow = 21;
    }

    /// <summary>Data cross-reference types — <c>dref_t</c>, xref.hpp.</summary>
    public static class Dref
    {
        /// <summary>The reference uses the address of the data rather than its value.</summary>
        public const int Offset = 1;

        public const int Write = 2;
        public const int Read = 3;
    }

    /// <summary>Flags for <c>set_segm_start</c> / <c>set_segm_end</c> — SEGMOD_, segment.hpp.</summary>
    public static class SegMod
    {
        public const int Kill = 0x0001;

        /// <summary>Keep the code and data already defined in the affected range.</summary>
        public const int Keep = 0x0002;

        public const int Silent = 0x0004;
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
