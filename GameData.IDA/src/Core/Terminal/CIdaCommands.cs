using System.Diagnostics;
using GameData.IDA.Shared.Ida;
using GameData.IDA.Shared.Interfaces;
using GameData.Tier0.Shared.Interfaces;
using GameData.Tier0.Shared.Logging;
using GameData.Tier0.Shared.Terminal;

namespace GameData.IDA.Core.Terminal;

internal static class CIdaCommands
{
    public static void Register()
    {
        _ = new ConCommand("ida_version", Version,
            "Initialize the IDA kernel if needed and print its version.");
        _ = new ConCommand("ida_open", Open,
            "Open a database. 'ida_open <path> [-noauto]' — auto-analysis runs unless -noauto is given.");
        _ = new ConCommand("ida_close", Close,
            "Close the open database. 'ida_close -save' to write changes back.");
        _ = new ConCommand("ida_batch", Batch,
            "Analyze several binaries at once, 'cores' at a time. " +
            "'ida_batch <file|dir|glob>...' — a directory is searched recursively, and a glob may " +
            "use '**' for the same. A database is written beside each input, so a second run over " +
            "the same files reopens them instead of reanalyzing.");
        _ = new ConCommand("ida_pool", Pool,
            "Show the analysis worker processes and what each is doing.");
    }

    private static void Batch(CommandContext ctx)
    {
        if (ctx.Args.Length == 0)
        {
            ctx.Warn("Usage: ida_batch <file|dir|glob>...");
            return;
        }

        var pool = InterfaceSystem.GetInterface<IIdaPool>(IdaInterfaceNames.IdaPool);
        if (pool == null)
        {
            ctx.Warn("No IIdaPool registered.");
            return;
        }

        var paths = Resolve(ctx.Args, out var unmatched);

        foreach (string argument in unmatched)
        {
            ctx.Warn($"Nothing matched '{argument}'.");
        }

        if (paths.Count == 0)
        {
            return;
        }

        ctx.Print($"Analyzing {paths.Count} file(s) across {Math.Min(pool.Size, paths.Count)} worker(s)...");

        var logging = InterfaceSystem.GetInterface<ILoggingSystem>(InterfaceNames.LoggingSystem);
        int channel = logging?.FindChannel("IDA") ?? -1;

        var bars = new Dictionary<int, ILoggingTask>();
        var clock = Stopwatch.StartNew();

        IReadOnlyList<IdaBatchItem> results;

        try
        {
            results = pool.RunBatch(paths, save: true, onProgress: progress =>
            {
                if (logging == null || channel < 0)
                {
                    return;
                }

                ILoggingTask bar;

                lock (bars)
                {
                    if (!bars.TryGetValue(progress.Worker, out bar!))
                    {
                        bar = logging.BeginProgress(channel, Path.GetFileName(progress.Path));
                        bars[progress.Worker] = bar;
                    }
                }

                string name = Path.GetFileName(progress.Path);

                if (progress.Finished)
                {
                    bar.Report(1.0, name);
                    return;
                }

                bar.Report(progress.Fraction, progress.Address == null
                    ? name
                    : $"{name} — {progress.Address}");
            });
        }
        finally
        {
            lock (bars)
            {
                foreach (var bar in bars.Values)
                {
                    bar.Complete();
                }
            }
        }

        Report(ctx, results, clock.Elapsed);
    }

    private static void Report(CommandContext ctx, IReadOnlyList<IdaBatchItem> results, TimeSpan elapsed)
    {
        int width = results.Max(r => Path.GetFileName(r.Path).Length);

        foreach (var result in results)
        {
            string name = Path.GetFileName(result.Path).PadRight(width);

            if (result.Succeeded)
            {
                ctx.Print($"  {name}  {result.Functions,6} functions  {result.Segments,4} segments  " +
                          $"{result.Elapsed.TotalSeconds,7:F1}s");
            }
            else
            {
                ctx.Warn($"  {name}  failed: {result.Error}");
            }
        }

        int ok = results.Count(r => r.Succeeded);
        double serial = results.Sum(r => r.Elapsed.TotalSeconds);

        ctx.Print($"{ok}/{results.Count} succeeded in {elapsed.TotalSeconds:F1}s " +
                  $"({serial:F1}s of analysis).");
    }

    private static void Pool(CommandContext ctx)
    {
        var pool = InterfaceSystem.GetInterface<IIdaPool>(IdaInterfaceNames.IdaPool);
        if (pool == null)
        {
            ctx.Warn("No IIdaPool registered.");
            return;
        }

        if (!pool.IsRunning)
        {
            ctx.Print($"No workers running. 'cores' is {pool.Size}; they start on the first ida_batch.");
            return;
        }

        foreach (var worker in pool.GetStatus())
        {
            string state = !worker.Alive ? "dead"
                : worker.CurrentPath == null ? "idle"
                : Path.GetFileName(worker.CurrentPath);

            ctx.Print($"  #{worker.Index}  {state,-32}  " +
                      $"{worker.Version ?? "?"} ({worker.Sdk ?? "?"})");
        }
    }

    private static List<string> Resolve(IEnumerable<string> args, out List<string> unmatched)
    {
        var paths = new List<string>();
        unmatched = [];

        foreach (string arg in args)
        {
            if (File.Exists(arg))
            {
                paths.Add(Path.GetFullPath(arg));
                continue;
            }

            if (Directory.Exists(arg))
            {
                var found = Enumerate(arg, "*", SearchOption.AllDirectories);
                paths.AddRange(found);

                if (found.Count == 0)
                {
                    unmatched.Add(arg);
                }

                continue;
            }

            string directory = Path.GetDirectoryName(arg) is { Length: > 0 } d ? d : ".";
            string pattern = Path.GetFileName(arg);
            var depth = SearchOption.TopDirectoryOnly;

            if (directory.Contains("**", StringComparison.Ordinal))
            {
                depth = SearchOption.AllDirectories;
                directory = directory[..directory.IndexOf("**", StringComparison.Ordinal)].TrimEnd('/', '\\');

                if (directory.Length == 0)
                {
                    directory = ".";
                }
            }

            var matches = Directory.Exists(directory) && pattern.Length > 0
                ? Enumerate(directory, pattern, depth)
                : [];

            if (matches.Count == 0)
            {
                unmatched.Add(arg);
            }

            paths.AddRange(matches);
        }

        return [.. paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Order()];
    }

    private static List<string> Enumerate(string directory, string pattern, SearchOption depth)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = depth == SearchOption.AllDirectories,
            IgnoreInaccessible = true,
        };

        return [.. Directory.EnumerateFiles(directory, pattern, options).Where(IsAnalyzable)];
    }

    private static bool IsAnalyzable(string path)
        => Path.GetExtension(path).ToLowerInvariant() is not (".i64" or ".idb" or ".id0" or ".id1"
            or ".id2" or ".nam" or ".til" or ".pat" or ".sig");

    private static void Version(CommandContext ctx)
    {
        if (!TryGetInitialized(ctx, out var ida))
        {
            return;
        }

        ctx.Print($"IDA {ida.GetVersion()} (bindings: {ida.SdkVersion})");
    }

    private static void Open(CommandContext ctx)
    {
        if (ctx.Args.Length == 0)
        {
            ctx.Warn("Usage: ida_open <path> [-noauto]");
            return;
        }

        if (!TryGetInitialized(ctx, out var ida))
        {
            return;
        }

        bool auto = !ctx.Args.Contains("-noauto", StringComparer.OrdinalIgnoreCase);
        string path = ctx.Args[0];

        if (!File.Exists(path))
        {
            ctx.Warn($"No such file: '{path}'");
            return;
        }

        ctx.Print($"Opening '{path}'{(auto ? " with auto-analysis" : string.Empty)}...");

        int status;

        var logging = InterfaceSystem.GetInterface<ILoggingSystem>(InterfaceNames.LoggingSystem);
        int channel = logging?.FindChannel("IDA") ?? -1;

        if (auto && logging != null && channel >= 0)
        {
            using var task = logging.BeginProgress(channel, $"Analyzing {Path.GetFileName(path)}");

            status = ida.Open(path, auto, progress =>
                task.Report(progress.Fraction, $"Analyzing {Path.GetFileName(path)} — {progress.Address:X}"));

            if (status == 0)
            {
                task.Complete();
            }
            else
            {
                task.Fail();
            }
        }
        else
        {
            status = ida.Open(path, auto);
        }

        if (status != 0)
        {
            ctx.Warn($"open_database() failed with status {status}.");
            return;
        }

        ctx.Print($"Opened. {ida.FunctionCount} functions, {ida.SegmentCount} segments.");
    }

    private static void Close(CommandContext ctx)
    {
        if (!TryGetLibrary(ctx, out var ida))
        {
            return;
        }

        if (!ida.IsDatabaseOpen)
        {
            ctx.Warn("No database is open.");
            return;
        }

        bool save = ctx.Args.Contains("-save", StringComparer.OrdinalIgnoreCase);
        ida.Close(save);
        ctx.Print(save ? "Closed and saved." : "Closed without saving.");
    }

    private static bool TryGetLibrary(CommandContext ctx, out IIdaLibrary ida)
    {
        var resolved = InterfaceSystem.GetInterface<IIdaLibrary>(IdaInterfaceNames.IdaLibrary);
        ida = resolved!;

        if (resolved != null)
        {
            return true;
        }

        ctx.Warn("No IIdaLibrary registered.");
        return false;
    }

    private static bool TryGetInitialized(CommandContext ctx, out IIdaLibrary ida)
    {
        if (!TryGetLibrary(ctx, out ida))
        {
            return false;
        }

        if (ida.TryInitialize(out string? error))
        {
            return true;
        }

        ctx.Warn(error ?? "The IDA kernel could not be initialized.");
        return false;
    }
}
