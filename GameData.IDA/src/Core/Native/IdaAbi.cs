using GameData.IDA.Shared.Ida;

namespace GameData.IDA.Core.Native;

/// <summary>
/// The hand-transcribed values that are not the same in every SDK version.
/// </summary>
/// <remarks>
/// <para>
/// Most of <see cref="IdaConstants"/> is bit flags, which the SDK does not renumber — a flag keeps
/// its bit for as long as it exists. The <c>inftag_t</c> tags are different: they are <em>ordinal
/// positions</em> in an enum, so inserting one member shifts every tag after it. Nothing catches
/// that. The build succeeds, <c>getinf</c> succeeds, and it returns a completely different field.
/// </para>
/// <para>
/// So they live here, behind the SDK version, instead of being compiled in as constants. Each
/// version's row has to be read off that version's <c>ida.hpp</c>; a version with no row is a
/// version this build must refuse rather than guess at.
/// </para>
/// </remarks>
public sealed record IdaAbi
{
    private static IdaAbi? _current;

    /// <summary>
    /// IDA 9.2 and 9.3, from <c>enum inftag_t</c> in ida.hpp. The two are the same table — read
    /// off both headers rather than assumed, which is the only way to know.
    /// </summary>
    private static readonly IdaAbi Ida92To93 = new()
    {
        MinEa = 19,
        MaxEa = 20,
        OriginalMinEa = 21,
        OriginalMaxEa = 22,
    };

    /// <summary>Tag for <c>getinf</c>: lowest address in the database.</summary>
    public required int MinEa { get; init; }

    /// <summary>Tag for <c>getinf</c>: one past the highest address in the database.</summary>
    public required int MaxEa { get; init; }

    /// <summary>Tag for <c>getinf</c>: lowest address straight after loading the input file.</summary>
    public required int OriginalMinEa { get; init; }

    /// <summary>Tag for <c>getinf</c>: one past the highest address straight after loading.</summary>
    public required int OriginalMaxEa { get; init; }

    /// <summary>The values for the SDK version the kernel was bound as.</summary>
    /// <exception cref="InvalidOperationException">No version has been selected yet.</exception>
    public static IdaAbi Current
        => _current ?? throw new InvalidOperationException(
            "The IDA kernel has not been initialized, so no SDK version has been selected.");

    /// <summary>The values transcribed for <paramref name="version"/>.</summary>
    /// <exception cref="NotSupportedException">That version has no transcribed row.</exception>
    public static IdaAbi For(IdaSdkVersion version) => version switch
    {
        IdaSdkVersion.V92 or IdaSdkVersion.V93 => Ida92To93,
        _ => throw new NotSupportedException(
            $"The inftag_t values for IDA {version} have not been transcribed. They are ordinal " +
            "positions in an SDK enum, so they have to be read off that version's ida.hpp rather " +
            "than assumed to match another release."),
    };

    /// <summary>Pins <see cref="Current"/> to the version the bindings were resolved against.</summary>
    internal static void Select(IdaSdkVersion version) => _current = For(version);
}
