using GameData.DepotDownloader.Shared.Depot;
using GameData.DepotDownloader.Shared.Interfaces;
using GameData.Tier0.Shared.Interfaces;
using GameData.Tier0.Shared.Logging;
using GameData.Tier0.Shared.Terminal;

namespace GameData.DepotDownloader.Core.Terminal;

internal static class CDepotCommands
{
    private const string Usage =
        "depot_download -app <id> -depot <id> [-branch <name>] [-manifest <id>] [-out <path>] [-file <pattern>]...";

    private static CancellationTokenSource? _active;
    private static Task? _activeTask;
    private static readonly Lock _lock = new();

    public static void Register()
    {
        _ = new ConCommand("depot_download", Download,
            "Download a depot. " + Usage);
        _ = new ConCommand("depot_cancel", Cancel,
            "Cancel the download started by depot_download.");
        _ = new ConCommand("depot_wait", Wait,
            "Block until the running download finishes. 'depot_wait <seconds>' to bound the wait.");
    }

    private static void Wait(CommandContext ctx)
    {
        Task? task;
        lock (_lock)
        {
            task = _activeTask;
        }

        if (task == null)
        {
            ctx.Warn("No download is running.");
            return;
        }

        int timeout = Timeout.Infinite;
        if (ctx.Args.Length > 0)
        {
            if (!int.TryParse(ctx.Args[0], out int seconds) || seconds <= 0)
            {
                ctx.Warn($"Invalid timeout: '{ctx.Args[0]}'");
                return;
            }

            timeout = seconds * 1000;
        }

        ctx.Print(task.Wait(timeout) ? "Download finished." : "Timed out waiting for download.");
    }

    private static void Download(CommandContext ctx)
    {
        if (!TryParse(ctx, out var request))
        {
            return;
        }

        var downloader = InterfaceSystem.GetInterface<IDepotDownloader>(DepotInterfaceNames.DepotDownloader);
        if (downloader == null)
        {
            ctx.Warn("No IDepotDownloader registered.");
            return;
        }

        var cancellation = new CancellationTokenSource();
        lock (_lock)
        {
            if (_active != null)
            {
                ctx.Warn("A download is already running; use depot_cancel first.");
                cancellation.Dispose();
                return;
            }

            _active = cancellation;
        }

        var log = InterfaceSystem.GetInterface<ILoggingSystem>(InterfaceNames.LoggingSystem);
        int channel = log?.FindChannel("Depot") ?? -1;

        ctx.Print($"Downloading app {request.AppId} depot {request.DepotId} " +
                  $"(branch '{request.Branch}') to {request.OutputPath}");

        var run = Task.Run(async () =>
        {
            var task = log?.BeginProgress(channel, $"depot {request.DepotId}");
            var progress = new Progress<DepotProgress>(p =>
            {
                if (task != null)
                {
                    task.Report(p.Fraction, p.CurrentFile is { } file
                        ? $"depot {request.DepotId} - {file}"
                        : $"depot {request.DepotId}");
                }
            });

            try
            {
                var result = await downloader
                    .DownloadAsync(request, progress, cancellation.Token)
                    .ConfigureAwait(false);

                if (result.Success)
                {
                    task?.Complete($"depot {result.DepotId} manifest {result.ManifestId} - " +
                                   $"{result.BytesDownloaded} bytes, {result.FilesDownloaded} files");
                }
                else
                {
                    task?.Fail($"depot {result.DepotId} failed: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                task?.Fail($"depot {request.DepotId} failed: {ex.Message}");
            }
            finally
            {
                lock (_lock)
                {
                    _active = null;
                }
                cancellation.Dispose();
            }
        });

        lock (_lock)
        {
            _activeTask = run;
        }
    }

    private static void Cancel(CommandContext ctx)
    {
        lock (_lock)
        {
            if (_active == null)
            {
                ctx.Warn("No download is running.");
                return;
            }

            _active.Cancel();
        }

        ctx.Print("Cancellation requested.");
    }

    private static bool TryParse(CommandContext ctx, out DepotDownloadRequest request)
    {
        request = null!;

        uint? appId = null;
        uint? depotId = null;
        ulong? manifestId = null;
        string branch = IDepotDownloader.DefaultBranch;
        string? outputPath = null;
        var files = new List<string>();

        var args = ctx.Args;
        for (int i = 0; i < args.Length; i++)
        {
            string flag = args[i];
            if (i + 1 >= args.Length)
            {
                ctx.Warn($"Missing value for '{flag}'. Usage: {Usage}");
                return false;
            }

            string value = args[++i];

            switch (flag)
            {
                case "-app":
                    if (!uint.TryParse(value, out uint app))
                    {
                        ctx.Warn($"Invalid app id: '{value}'");
                        return false;
                    }
                    appId = app;
                    break;

                case "-depot":
                    if (!uint.TryParse(value, out uint depot))
                    {
                        ctx.Warn($"Invalid depot id: '{value}'");
                        return false;
                    }
                    depotId = depot;
                    break;

                case "-manifest":
                    if (!ulong.TryParse(value, out ulong manifest))
                    {
                        ctx.Warn($"Invalid manifest id: '{value}'");
                        return false;
                    }
                    manifestId = manifest;
                    break;

                case "-branch":
                    branch = value;
                    break;

                case "-out":
                    outputPath = value;
                    break;

                case "-file":
                    files.Add(value);
                    break;

                default:
                    ctx.Warn($"Unknown option '{flag}'. Usage: {Usage}");
                    return false;
            }
        }

        if (appId == null || depotId == null)
        {
            ctx.Warn($"-app and -depot are required. Usage: {Usage}");
            return false;
        }

        request = new DepotDownloadRequest
        {
            AppId = appId.Value,
            DepotId = depotId.Value,
            OutputPath = outputPath ?? Path.Combine("depots", appId.Value.ToString(), depotId.Value.ToString()),
            Branch = branch,
            ManifestId = manifestId,
            FileList = files.Count > 0 ? files : null,
        };

        return true;
    }
}
