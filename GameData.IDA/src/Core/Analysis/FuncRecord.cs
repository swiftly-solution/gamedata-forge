using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GameData.IDA.Core.Native;

namespace GameData.IDA.Core.Analysis;

/// <summary>
/// The layout of <c>func_t</c> from funcs.hpp, mirrored so functions can be created and their
/// flags edited.
/// </summary>
/// <remarks>
/// <para>
/// Two things need it. Reading and writing <see cref="Flags"/> — <c>get_func_flags</c> and
/// <c>set_func_attr</c> are IDAPython-only, so the flags word is reachable only through the
/// <c>func_t *</c> the kernel hands back. And <c>add_func_ex</c>, which is the only exported way to
/// create a function: the familiar <c>add_func(ea1, ea2)</c> is an inline that stack-constructs one
/// of these and calls it.
/// </para>
/// <para>
/// Safe to mirror because <c>func_t</c> is plain layout — it derives from <c>range_t</c>, has no
/// virtual functions and no C++ containers, only scalars and raw pointers. The union at the tail is
/// laid out here as its larger arm; the smaller one (<c>owner</c>/<c>refqty</c>/<c>referers</c>,
/// used for function tails) overlaps it and is never written by this code.
/// </para>
/// <para>
/// Verified byte-identical between the 9.2 and 9.3 SDKs. It is named differently from the SDK type
/// on purpose, following <see cref="CompiledBinPattern"/>: the generator keeps emitting
/// <c>func_t</c> as an opaque struct and call sites cast, so adding this costs no regeneration.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct FuncRecord
{
    internal ulong StartEa;

    internal ulong EndEa;

    /// <summary>See <see cref="IdaConstants.FuncFlags"/>.</summary>
    internal ulong Flags;

    internal ulong Frame;
    internal ulong FrameSize;
    internal ushort SavedRegisterSize;
    internal ulong ArgumentSize;
    internal ulong FramePointerDelta;
    internal uint Color;
    internal uint StackPointChangeCount;
    internal nint StackPointChanges;
    internal int RegisterVariableCount;
    internal nint RegisterVariables;
    internal int LocalLabelCount;
    internal nint LocalLabels;
    internal int RegisterArgumentCount;
    internal nint RegisterArguments;
    internal int TailCount;
    internal nint Tails;

    /// <summary>
    /// A record initialised the way <c>func_t</c>'s own constructor would, ready for
    /// <c>add_func_ex</c>.
    /// </summary>
    /// <remarks>
    /// Zeroing is not equivalent: the constructor sets <c>frame</c> to <c>BADNODE</c> and
    /// <c>color</c> to <c>DEFCOLOR</c>, both all-ones rather than zero, and marks the non-return
    /// analysis as still pending. A zero-filled record would claim frame node 0 and colour 0.
    /// </remarks>
    internal static FuncRecord Create(ulong start, ulong end) => new()
    {
        StartEa = start,
        EndEa = end,
        Flags = IdaConstants.FuncFlags.NoRetPending,
        Frame = BadNode,
        Color = DefaultColor,
    };

    /// <summary><c>BADNODE</c> from netnode.hpp — <c>nodeidx_t(-1)</c>.</summary>
    private const ulong BadNode = ulong.MaxValue;

    /// <summary><c>DEFCOLOR</c> from pro.h — <c>bgcolor_t(-1)</c>.</summary>
    private const uint DefaultColor = uint.MaxValue;
}

/// <summary>Reads and writes the fields of a <c>func_t</c> the kernel owns.</summary>
internal static unsafe class FuncRecordAccess
{
    /// <summary>The flags word of a function the kernel handed back.</summary>
    internal static ulong GetFlags(func_t* function) => ((FuncRecord*)function)->Flags;

    /// <summary>
    /// Adds <paramref name="flags"/> to a function and commits the change.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> if the function already had them, so nothing was written.
    /// </returns>
    internal static bool AddFlags(func_t* function, ulong flags)
    {
        var record = (FuncRecord*)function;

        if ((record->Flags & flags) == flags)
        {
            return false;
        }

        record->Flags |= flags;

        // The kernel caches function attributes, so a write straight into its own structure is not
        // visible until it is told to re-read it.
        return Ida.update_func(function) != 0;
    }

    /// <summary>Passes a locally built record where the SDK wants a <c>func_t *</c>.</summary>
    internal static func_t* AsPointer(ref FuncRecord record)
        => (func_t*)Unsafe.AsPointer(ref record);
}
