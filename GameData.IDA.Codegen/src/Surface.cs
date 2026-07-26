using System.Globalization;

namespace GameData.IDA.Codegen;

/// <summary>One vendored SDK, discovered as a subdirectory of the SDK root.</summary>
/// <remarks>
/// The directory name is the version key and is written <c>major.minor</c> — <c>9.2</c>. It is what
/// ties a vendored header tree to an <c>IdaSdkVersion</c> member in the runtime library, so the two
/// only have to agree on that one string.
/// </remarks>
internal sealed record SdkVersion(string Key, string Root, int Major, int Minor)
{
    /// <summary>The C# identifier suffix for this version: <c>9.2</c> becomes <c>V92</c>.</summary>
    internal string Identifier => "V" + Key.Replace(".", string.Empty, StringComparison.Ordinal);

    internal string Include => Path.Combine(Root, "include");

    internal string Exports => Path.Combine(Root, "exports");

    /// <summary>The full SDK release string from the version's <c>VERSION</c> file, if it has one.</summary>
    internal string Label
    {
        get
        {
            string file = Path.Combine(Root, "VERSION");
            return File.Exists(file) ? File.ReadAllText(file).Trim() : Key;
        }
    }

    public override string ToString() => Key;

    /// <summary>
    /// Finds every vendored SDK under <paramref name="root"/>, oldest first.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A subdirectory is not named <c>major.minor</c>, or is missing <c>include</c> or <c>exports</c>.
    /// Silently skipping either would generate a build that quietly lacks a version.
    /// </exception>
    internal static IReadOnlyList<SdkVersion> Discover(string root)
    {
        var versions = new List<SdkVersion>();

        foreach (string directory in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
        {
            string key = Path.GetFileName(directory);
            string[] parts = key.Split('.');

            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
            {
                throw new InvalidOperationException(
                    $"'{directory}' is not a version directory. Each subdirectory of the SDK root is " +
                    "one vendored SDK and must be named 'major.minor', as in '9.2'.");
            }

            var version = new SdkVersion(key, directory, major, minor);

            foreach (string required in (string[])[version.Include, version.Exports])
            {
                if (!Directory.Exists(required))
                {
                    throw new InvalidOperationException(
                        $"The vendored SDK '{key}' has no '{Path.GetFileName(required)}' directory. " +
                        "Every version needs both 'include' and 'exports'.");
                }
            }

            versions.Add(version);
        }

        return [.. versions.OrderBy(v => v.Major).ThenBy(v => v.Minor)];
    }
}

/// <summary>A declaration the generator could not emit, and why.</summary>
internal sealed record Skipped(string Header, string Name, string Reason, string Signature);

/// <summary>One parameter of a <see cref="RenderedDecl"/>, already mapped to C#.</summary>
internal sealed record RenderedParam(string Type, string Name, string? DefaultValue);

/// <summary>
/// An exported declaration with every C type already mapped to C#. This is the form two SDK
/// versions are compared in: whether one field and one forwarder can serve both is a question
/// about the rendered signature, not about the header text it came from.
/// </summary>
internal sealed record RenderedDecl(
    string Header,
    string Name,
    string Module,
    string Method,
    string Field,
    string ReturnType,
    IReadOnlyList<RenderedParam> Parameters,
    bool IsData,
    string RawSignature)
{
    /// <summary>The C# type of the static field this declaration binds to.</summary>
    /// <remarks>
    /// A data export binds one level more indirectly than the C declaration reads:
    /// <c>GetExport</c> on a data symbol yields the address of the variable.
    /// </remarks>
    internal string FieldType => IsData
        ? ReturnType + "*"
        : $"delegate* unmanaged[Cdecl]<{string.Join(", ", Parameters.Select(p => p.Type).Append(ReturnType))}>";

    /// <summary>The handle name the binder passes for this declaration's module.</summary>
    internal string Handle => Module.Equals("idalib", StringComparison.OrdinalIgnoreCase) ? "idalib" : "ida";
}

/// <summary>One symbol of the merged binding surface and the versions that export it.</summary>
/// <param name="Decl">The newest version's rendering, which is what gets emitted.</param>
/// <param name="Versions">Every version whose export table carries this symbol, oldest first.</param>
internal sealed record SurfaceEntry(RenderedDecl Decl, IReadOnlyList<SdkVersion> Versions)
{
    /// <summary>Whether every vendored SDK exports this symbol, so the forwarder needs no guard.</summary>
    internal bool IsUniversal(int versionCount) => Versions.Count == versionCount;
}

/// <summary>A symbol whose rendered signature is not the same in every version that declares it.</summary>
internal sealed record SurfaceConflict(string Name, IReadOnlyList<(SdkVersion Version, string FieldType)> Renderings);
