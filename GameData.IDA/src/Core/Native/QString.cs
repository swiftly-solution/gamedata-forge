using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace GameData.IDA.Core.Native;

/// <summary>
/// The layout every IDA container shares: <c>qvector&lt;T&gt;</c> is a pointer, a used count and
/// a capacity, all three pointer-sized on x64.
/// </summary>
/// <remarks>
/// Declared once and reused for every specialisation, because managed code only ever needs the
/// three header fields. Element access goes through <see cref="Array"/> with the caller's own
/// element type.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct qvector
{
    public void* Array;
    public nuint Count;
    public nuint Capacity;
}

/// <summary>
/// <c>qstring</c>, which is a <c>qvector&lt;char&gt;</c> holding UTF-8 bytes.
/// </summary>
/// <remarks>
/// <see cref="Count"/> includes the terminating NUL whenever the string is non-empty, so the text
/// length is one less. Hand-written rather than generated: the 123 exported functions that fill a
/// <c>qstring *</c> out-parameter need a real layout, not an opaque handle.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct qstring
{
    public byte* Array;
    public nuint Count;
    public nuint Capacity;
}

/// <summary><c>qwstring</c> — a <c>qvector&lt;wchar16_t&gt;</c> of UTF-16 code units.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct qwstring
{
    public ushort* Array;
    public nuint Count;
    public nuint Capacity;
}

/// <summary><c>qstrvec_t</c> — a <c>qvector&lt;qstring&gt;</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct qstrvec_t
{
    public qstring* Array;
    public nuint Count;
    public nuint Capacity;
}

/// <summary>
/// The common base of <c>func_t</c> and <c>segment_t</c>. Neither has virtual functions, so the
/// two address fields sit at offset zero of both and a <c>func_t*</c> can be read through this.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct range_t
{
    public ulong StartEa;
    public ulong EndEa;
}

/// <summary>
/// The contents of IDA's auto-analysis indicator, from <c>auto_display_t</c> in auto.hpp.
/// </summary>
/// <remarks>
/// Hand-written because the generator emits functions only, and <c>get_auto_display</c> is the
/// one call whose out-parameter has to be a real layout rather than an opaque handle. Both
/// <c>atype_t</c> and <c>idastate_t</c> are <c>int</c>; <c>ea</c> is 8-byte aligned between them.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct auto_display_t
{
    /// <summary>The AU_ analysis type currently being run.</summary>
    public int Type;

    /// <summary>The address being analyzed, or <see cref="IdaConstants.BadAddress"/> when idle.</summary>
    public ulong Ea;

    /// <summary>The st_ kernel state.</summary>
    public int State;
}

/// <summary>
/// Owns a <see cref="qstring"/> for the duration of one call, so that the buffer IDA allocates
/// with its own allocator is released with that same allocator.
/// </summary>
/// <example>
/// <code>
/// using var buffer = new QStringBuffer();
/// return Ida.get_func_name(buffer.Pointer, ea) > 0 ? buffer.ToString() : null;
/// </code>
/// </example>
public unsafe ref struct QStringBuffer
{
    private qstring _value;
    private bool _disposed;

    /// <summary>Pass this to any exported function taking a <c>qstring *</c> out-parameter.</summary>
    public qstring* Pointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, typeof(QStringBuffer));
            return (qstring*)Unsafe.AsPointer(ref _value);
        }
    }

    /// <summary>Length of the text in bytes, excluding the terminating NUL.</summary>
    public int Length
    {
        get
        {
            if (_value.Array == null || _value.Count == 0)
            {
                return 0;
            }

            // IDA counts the terminator; anything else would be a string with an embedded NUL,
            // which the SDK does not produce for the functions this wraps.
            int length = (int)_value.Count;
            return _value.Array[length - 1] == 0 ? length - 1 : length;
        }
    }

    public override string ToString()
        => _value.Array == null ? string.Empty : Encoding.UTF8.GetString(_value.Array, Length);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_value.Array != null)
        {
            Ida.qfree(_value.Array);
            _value = default;
        }
    }
}

/// <summary>UTF-8 conversion helpers. IDA is UTF-8 internally, in both directions.</summary>
public static unsafe class Utf8
{
    /// <summary>
    /// Copies <paramref name="value"/> into unmanaged memory as a NUL-terminated UTF-8 string.
    /// The caller owns the result and must release it with <see cref="Free"/>.
    /// </summary>
    public static byte* Allocate(string? value)
        => value == null ? null : (byte*)Marshal.StringToCoTaskMemUTF8(value);

    public static void Free(byte* value)
    {
        if (value != null)
        {
            Marshal.FreeCoTaskMem((nint)value);
        }
    }

    /// <summary>Reads a NUL-terminated UTF-8 string, or <see langword="null"/> for a null pointer.</summary>
    public static string? ToManaged(byte* value)
        => value == null ? null : Marshal.PtrToStringUTF8((nint)value);
}
