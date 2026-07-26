using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GameData.IDA.Core.Native;

/// <summary>
/// The layout of <c>compiled_binpat_t</c> from bytes.hpp — three <c>qvector</c>s and an encoding
/// index — mirrored so a pattern compiled by <c>parse_binpat_str</c> can be freed again.
/// </summary>
/// <remarks>
/// The SDK exports no destructor for this type, so the buffers it allocates have to be released
/// field by field. Getting this wrong leaks on every pattern search, which is exactly the code
/// path that runs in a loop.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct CompiledBinPattern
{
    internal qvector Bytes;
    internal qvector Mask;
    internal qvector StringLiterals;
    internal int EncodingIndex;
}

/// <summary>
/// Owns a <c>compiled_binpat_vec_t</c> — a <c>qvector&lt;compiled_binpat_t&gt;</c> — for the
/// duration of one search.
/// </summary>
internal unsafe ref struct CompiledPatternVector
{
    private qvector _value;
    private bool _disposed;

    /// <summary>Pass this where the SDK wants a <c>compiled_binpat_vec_t *</c>.</summary>
    internal compiled_binpat_vec_t* Pointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, typeof(CompiledPatternVector));
            return (compiled_binpat_vec_t*)Unsafe.AsPointer(ref _value);
        }
    }

    internal bool IsEmpty => _value.Count == 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_value.Array == null)
        {
            return;
        }

        var items = (CompiledBinPattern*)_value.Array;
        for (nuint i = 0; i < _value.Count; i++)
        {
            Ida.qfree(items[i].Bytes.Array);
            Ida.qfree(items[i].Mask.Array);
            Ida.qfree(items[i].StringLiterals.Array);
        }

        Ida.qfree(_value.Array);
        _value = default;
    }
}
