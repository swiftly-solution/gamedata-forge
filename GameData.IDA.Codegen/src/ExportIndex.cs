using System.Text.RegularExpressions;

namespace GameData.IDA.Codegen;

/// <summary>
/// The set of symbols the shipped libraries actually export, read from committed dumps.
/// </summary>
/// <remarks>
/// <para>
/// This is what lets the generated binder resolve every symbol eagerly and let
/// <c>NativeLibrary.GetExport</c> throw on a miss: nothing is emitted that was not observed in a
/// real export table, so a failure at runtime means the libraries on disk are not the build the
/// bindings were generated against.
/// </para>
/// <para>
/// Modules are identified by the dump's file stem — <c>ida</c> and <c>idalib</c> — not by a file
/// name, because the library those refer to is <c>ida.dll</c>, <c>libida.so</c> or
/// <c>libida.dylib</c> depending on where the generated code ends up running.
/// </para>
/// <para>
/// Three dump shapes are accepted: <c>dumpbin /EXPORTS</c> over a DLL, the same over an import
/// library, and <c>nm --dynamic --defined-only</c>. Which one a dump happens to be is an accident
/// of what the machine that produced it had to hand — an SDK download ships <c>.lib</c> files, an
/// installation ships the <c>.dll</c> — and says nothing about the symbols themselves.
/// </para>
/// </remarks>
internal sealed partial class ExportIndex
{
    private readonly Dictionary<string, string> _symbolToModule = new(StringComparer.Ordinal);

    /// <summary>dumpbin names itself on its first line; nm output has no header at all.</summary>
    [GeneratedRegex(@"^Microsoft \(R\) COFF/PE Dumper", RegexOptions.Multiline)]
    private static partial Regex DumpbinBanner { get; }

    /// <summary>
    /// The column header that opens dumpbin's export table, in either layout it emits:
    /// <c>ordinal hint RVA      name</c> for a DLL, <c>ordinal    name</c> for an import library.
    /// </summary>
    [GeneratedRegex(@"^\s*ordinal\b.*\bname\s*$")]
    private static partial Regex ExportTableHeader { get; }

    /// <summary>A DLL row: ordinal, hint, RVA, name.</summary>
    [GeneratedRegex(@"^\s*\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]{8}\s+(?<name>\S+)\s*$")]
    private static partial Regex DllRow { get; }

    /// <summary>
    /// An import-library row, which is the name alone: a <c>.lib</c> records what a symbol is
    /// called but not where it will live, so the ordinal and address columns are left empty.
    /// </summary>
    [GeneratedRegex(@"^\s+(?<name>\S+)\s*$")]
    private static partial Regex LibraryRow { get; }

    /// <summary>
    /// A row of <c>nm --dynamic --defined-only</c>: address, symbol type, name. Only the types
    /// that denote an exported definition are taken — <c>T</c>/<c>t</c> for code, <c>D</c>/<c>B</c>
    /// for data, <c>W</c> for weak, <c>i</c>/<c>R</c> for indirect and read-only data.
    /// </summary>
    [GeneratedRegex(@"^(?<addr>[0-9A-Fa-f]+)?\s+(?<type>[TtDdBbWwiIRr])\s+(?<name>\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex NmRow { get; }

    /// <summary>Loads every <c>*.exports.txt</c> in <paramref name="exportsDir"/>.</summary>
    internal static ExportIndex Load(string exportsDir)
    {
        var index = new ExportIndex();

        foreach (string file in Directory.EnumerateFiles(exportsDir, "*.exports.txt").OrderBy(f => f))
        {
            // "idalib.exports.txt" describes the module "idalib".
            string module = Path.GetFileName(file).Replace(".exports.txt", string.Empty, StringComparison.Ordinal);
            string text = File.ReadAllText(file);

            foreach (string symbol in ParseSymbols(text))
            {
                index._symbolToModule.TryAdd(symbol, module);
            }
        }

        if (index._symbolToModule.Count == 0)
        {
            throw new InvalidOperationException(
                $"No export rows parsed from '{exportsDir}'. Expected 'dumpbin /EXPORTS' output over " +
                "a DLL or an import library, or 'nm --dynamic --defined-only' output, in " +
                "<module>.exports.txt files.");
        }

        return index;
    }

    private static IEnumerable<string> ParseSymbols(string text)
        => DumpbinBanner.IsMatch(text) ? ParseDumpbin(text) : ParseNm(text);

    /// <summary>
    /// Reads dumpbin's export table as a section rather than as a pattern over the whole file.
    /// </summary>
    /// <remarks>
    /// An import-library row is a bare identifier on an indented line, which is also what
    /// dumpbin's own section labels look like — <c>Exports</c> and <c>Summary</c> would both parse
    /// as symbols. Bounding the search by the column header and the summary is what tells the two
    /// apart, and it costs nothing for the DLL layout, whose rows are unambiguous either way.
    /// </remarks>
    private static IEnumerable<string> ParseDumpbin(string text)
    {
        bool inTable = false;

        foreach (string line in text.Split('\n'))
        {
            string row = line.TrimEnd('\r');

            if (!inTable)
            {
                inTable = ExportTableHeader.IsMatch(row);
                continue;
            }

            // dumpbin closes the export table with its per-section byte summary.
            if (row.AsSpan().Trim() is "Summary")
            {
                yield break;
            }

            if (row.AsSpan().Trim().IsEmpty)
            {
                continue;
            }

            var match = DllRow.Match(row);
            if (!match.Success)
            {
                match = LibraryRow.Match(row);
            }

            if (match.Success)
            {
                yield return match.Groups["name"].Value;
            }
        }
    }

    private static IEnumerable<string> ParseNm(string text)
    {
        foreach (Match row in NmRow.Matches(text))
        {
            yield return row.Groups["name"].Value;
        }
    }

    internal int Count => _symbolToModule.Count;

    /// <summary>Resolves a symbol to the module that exports it: <c>ida</c> or <c>idalib</c>.</summary>
    internal bool TryGetModule(string symbol, out string module)
        => _symbolToModule.TryGetValue(symbol, out module!);
}
