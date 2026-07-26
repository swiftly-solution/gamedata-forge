using System.Text;
using GameData.IDA.Codegen;

string sdkRoot = Path.Combine("thirdparty", "ida-sdk");
string output = Path.Combine("GameData.IDA", "src", "Core", "Native", "Generated");

for (int i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--sdk-root": sdkRoot = args[++i]; break;
        case "--out": output = args[++i]; break;
    }
}

if (!Directory.Exists(sdkRoot))
{
    Console.Error.WriteLine($"SDK root directory not found: '{Path.GetFullPath(sdkRoot)}'");
    Console.Error.WriteLine("Usage: GameData.IDA.Codegen --sdk-root <sdk-root> --out <generated-dir>");
    Console.Error.WriteLine("The SDK root holds one directory per vendored SDK, named 'major.minor'.");
    return 1;
}

IReadOnlyList<SdkVersion> versions;

try
{
    versions = SdkVersion.Discover(sdkRoot);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

if (versions.Count == 0)
{
    Console.Error.WriteLine($"No vendored SDK found under '{Path.GetFullPath(sdkRoot)}'.");
    return 1;
}

var scans = new Dictionary<string, ScanResult>(StringComparer.Ordinal);
var exportCounts = new Dictionary<string, int>(StringComparer.Ordinal);
var renderers = new Dictionary<string, DeclRenderer>(StringComparer.Ordinal);
var unresolved = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
var rendered = new Dictionary<string, IReadOnlyList<RenderedDecl>>(StringComparer.Ordinal);
var opaque = new SortedSet<string>(StringComparer.Ordinal);
var ordered = new List<(SdkVersion Version, IReadOnlyList<RenderedDecl> Declarations)>();

foreach (var version in versions)
{
    var scan = HeaderScanner.Scan(version.Include);
    var exports = ExportIndex.Load(version.Exports);
    var types = new TypeTable(scan);
    var renderer = new DeclRenderer(types, exports);

    var declarations = renderer.Render(scan.Declarations);

    scans[version.Key] = scan;
    exportCounts[version.Key] = exports.Count;
    renderers[version.Key] = renderer;
    unresolved[version.Key] = types.Unresolved;
    rendered[version.Key] = declarations;
    ordered.Add((version, declarations));

    // The opaque set is only populated as TryRender reaches each type, so it has to be collected
    // after rendering — and unioned, because a type may only be reachable in one version.
    opaque.UnionWith(types.OpaqueTypes);
}

var surface = SurfaceMerger.Merge(ordered);
var emitter = new Emitter(output);

emitter.Emit(surface, versions, rendered, opaque);

var report = new StringBuilder();
report.AppendLine("GameData.IDA.Codegen report");
report.AppendLine($"  generated : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
report.AppendLine($"  sdk root  : {Path.GetFullPath(sdkRoot)}");
report.AppendLine($"  versions  : {string.Join(", ", versions.Select(v => v.Label))}");
report.AppendLine();
report.AppendLine($"  surface functions     : {emitter.EmittedCount}");
report.AppendLine($"  surface data exports  : {emitter.EmittedDataCount}");
report.AppendLine($"  version-guarded       : {emitter.GuardedCount}");
report.AppendLine($"  signature conflicts   : {surface.Conflicts.Count}");
report.AppendLine($"  opaque types emitted  : {opaque.Count}");
report.AppendLine();

foreach (var version in versions)
{
    var scan = scans[version.Key];
    var renderer = renderers[version.Key];

    report.AppendLine($"IDA {version.Key} ({version.Label})");
    report.AppendLine($"  exports    : {exportCounts[version.Key]} symbols across the committed dumps");
    report.AppendLine($"  declarations scanned : {scan.Declarations.Count}");
    report.AppendLine($"  rendered             : {rendered[version.Key].Count}");
    report.AppendLine($"  bound by this version: {emitter.BoundPerVersion[version.Key]}");
    report.AppendLine($"  skipped              : {renderer.SkippedDeclarations.Count}");
    report.AppendLine($"  malformed            : {scan.Malformed.Count}");
    report.AppendLine($"  unresolved base types: {unresolved[version.Key].Count}");

    foreach (string name in unresolved[version.Key])
    {
        report.AppendLine($"      {name}");
    }

    report.AppendLine();
}

if (surface.Conflicts.Count > 0)
{
    report.AppendLine("Symbols whose rendered signature differs between SDK versions. These are left off");
    report.AppendLine("the surface entirely: binding one shape and calling it on the other version would");
    report.AppendLine("corrupt the stack silently. Each needs a hand-written per-version shim.");
    report.AppendLine();

    foreach (var conflict in surface.Conflicts.OrderBy(c => c.Name, StringComparer.Ordinal))
    {
        report.AppendLine($"  {conflict.Name}");
        foreach (var (version, fieldType) in conflict.Renderings)
        {
            report.AppendLine($"      {version.Key,-6} {fieldType}");
        }
    }

    report.AppendLine();
}

foreach (var version in versions)
{
    var scan = scans[version.Key];
    var renderer = renderers[version.Key];

    if (scan.Malformed.Count > 0)
    {
        report.AppendLine($"IDA {version.Key} — declarations the scanner rejected ({scan.Malformed.Count}):");
        foreach (string line in scan.Malformed)
        {
            report.AppendLine($"  {line}");
        }

        report.AppendLine();
    }

    report.AppendLine($"IDA {version.Key} — skipped declarations, grouped by reason:");
    foreach (var group in renderer.SkippedDeclarations
                 .GroupBy(s => s.Reason)
                 .OrderByDescending(g => g.Count()))
    {
        report.AppendLine($"  [{group.Count()}] {group.Key}");
        foreach (var skipped in group.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            report.AppendLine($"      {skipped.Header,-22} {skipped.Name}");
        }
    }

    report.AppendLine();
}

Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "_codegen-report.txt"), report.ToString());

Console.WriteLine($"{versions.Count} SDK version(s): {string.Join(", ", versions.Select(v => v.Key))}");
Console.WriteLine($"surface: {emitter.EmittedCount} functions + {emitter.EmittedDataCount} data exports, " +
                  $"{emitter.GuardedCount} version-guarded, {surface.Conflicts.Count} conflicts");

foreach (var version in versions)
{
    Console.WriteLine($"  {version.Key}: binds {emitter.BoundPerVersion[version.Key]}, " +
                      $"skipped {renderers[version.Key].SkippedDeclarations.Count}");
}

Console.WriteLine($"output: {Path.GetFullPath(output)}");

return 0;
