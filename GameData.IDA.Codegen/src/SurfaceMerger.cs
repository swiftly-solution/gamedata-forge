namespace GameData.IDA.Codegen;

/// <summary>The one binding surface every vendored SDK version is bound through.</summary>
/// <param name="Entries">Symbols to emit, grouped and ordered by the newest version's header.</param>
/// <param name="Conflicts">Symbols left out because their rendered signature is version-dependent.</param>
internal sealed record MergedSurface(IReadOnlyList<SurfaceEntry> Entries, IReadOnlyList<SurfaceConflict> Conflicts);

/// <summary>
/// Folds every vendored version's rendered declarations into a single binding surface.
/// </summary>
/// <remarks>
/// <para>
/// The class <c>Ida</c> holds one static field per symbol, so a symbol can only appear once no
/// matter how many SDK versions are vendored. That works as long as every version renders it the
/// same way, which for a minor SDK revision is nearly always true — what changes between them is
/// which symbols exist, not what their signatures are.
/// </para>
/// <para>
/// When a signature genuinely does differ, the symbol is dropped from the surface and reported
/// rather than resolved in favour of one version. Picking one would compile fine and then call
/// through the wrong signature on the other version, which is a silent stack corruption; a hand
/// written per-version shim is the only correct answer, and this makes the generator ask for one.
/// </para>
/// </remarks>
internal static class SurfaceMerger
{
    internal static MergedSurface Merge(IReadOnlyList<(SdkVersion Version, IReadOnlyList<RenderedDecl> Declarations)> versions)
    {
        // Insertion order is the oldest version's header order, with symbols new in later versions
        // appended as they first appear. Emission regroups by header, so this only decides the
        // order within a header group.
        var byName = new Dictionary<string, List<(SdkVersion Version, RenderedDecl Decl)>>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var (version, declarations) in versions)
        {
            foreach (var decl in declarations)
            {
                if (!byName.TryGetValue(decl.Name, out var observations))
                {
                    observations = [];
                    byName[decl.Name] = observations;
                    order.Add(decl.Name);
                }

                observations.Add((version, decl));
            }
        }

        var entries = new List<SurfaceEntry>(order.Count);
        var conflicts = new List<SurfaceConflict>();

        foreach (string name in order)
        {
            var observations = byName[name];
            string shape = observations[^1].Decl.FieldType;

            if (observations.Any(o => !string.Equals(o.Decl.FieldType, shape, StringComparison.Ordinal)))
            {
                conflicts.Add(new SurfaceConflict(
                    name,
                    [.. observations.Select(o => (o.Version, o.Decl.FieldType))]));
                continue;
            }

            // The newest version wins for everything cosmetic — the header the symbol is emitted
            // under, its parameter names, its documented C declaration and its defaults.
            entries.Add(new SurfaceEntry(observations[^1].Decl, [.. observations.Select(o => o.Version)]));
        }

        return new MergedSurface(entries, conflicts);
    }
}
