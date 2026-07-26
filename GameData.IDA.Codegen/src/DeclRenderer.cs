namespace GameData.IDA.Codegen;

/// <summary>
/// Turns one SDK version's scanned declarations into <see cref="RenderedDecl"/>s, dropping the ones
/// that cannot be expressed in C# or were never observed in that version's export table.
/// </summary>
/// <remarks>
/// Rendering is per version because the type universe is: a <see cref="TypeTable"/> is built from
/// one version's headers and knows nothing about any other. Comparing versions therefore has to
/// happen after this step, on the rendered form — see <see cref="SurfaceMerger"/>.
/// </remarks>
internal sealed class DeclRenderer(TypeTable types, ExportIndex exports)
{
    private readonly List<Skipped> _skipped = [];

    internal IReadOnlyList<Skipped> SkippedDeclarations => _skipped;

    /// <summary>
    /// Renders every declaration that survives, grouped by header and in header order so the
    /// emitted files stay stable between runs.
    /// </summary>
    internal IReadOnlyList<RenderedDecl> Render(IReadOnlyList<NativeDecl> declarations)
    {
        var rendered = new List<RenderedDecl>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in declarations
                     .GroupBy(d => d.Header)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var decl in group)
            {
                if (!exports.TryGetModule(decl.Name, out string module))
                {
                    _skipped.Add(new Skipped(decl.Header, decl.Name, "declared but not exported", decl.RawSignature));
                    continue;
                }

                if (!seen.Add(decl.Name))
                {
                    _skipped.Add(new Skipped(decl.Header, decl.Name, "duplicate declaration", decl.RawSignature));
                    continue;
                }

                if (TryRender(decl, module, out var result))
                {
                    rendered.Add(result);
                }
                else
                {
                    seen.Remove(decl.Name);
                }
            }
        }

        return rendered;
    }

    private bool TryRender(NativeDecl decl, string module, out RenderedDecl rendered)
    {
        rendered = null!;

        if (!types.TryRender(decl.ReturnType, out string returnType))
        {
            _skipped.Add(new Skipped(decl.Header, decl.Name, $"unmappable return type '{decl.ReturnType.BaseName}'", decl.RawSignature));
            return false;
        }

        var parameters = new List<RenderedParam>(decl.Parameters.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parameter in decl.Parameters)
        {
            if (!types.TryRender(parameter.Type, out string type))
            {
                _skipped.Add(new Skipped(decl.Header, decl.Name, $"unmappable parameter type '{parameter.Type.BaseName}'", decl.RawSignature));
                return false;
            }

            string name = TypeTable.Sanitize(parameter.Name);
            while (!used.Add(name))
            {
                name += "_";
            }

            parameters.Add(new RenderedParam(type, name, parameter.DefaultValue));
        }

        rendered = new RenderedDecl(
            decl.Header,
            decl.Name,
            module,
            TypeTable.Sanitize(decl.Name),
            "_" + decl.Name,
            returnType,
            parameters,
            decl.IsData,
            decl.RawSignature);

        return true;
    }
}
