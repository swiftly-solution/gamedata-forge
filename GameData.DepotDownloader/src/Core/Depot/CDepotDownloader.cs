using System.Globalization;
using System.Text.RegularExpressions;
using GameData.DepotDownloader.Shared.Depot;
using GameData.DepotDownloader.Shared.Interfaces;
using GameData.Tier0.Shared.Interfaces;
using GameData.Tier0.Shared.Logging;

namespace GameData.DepotDownloader.Core.Depot;

[ExposeInterface(DepotInterfaceNames.DepotDownloader)]
internal sealed partial class CDepotDownloader : IDepotDownloader
{
    private const string AccountSettingsFile = "account.config";

    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static bool _accountSettingsLoaded;

    public async Task<DepotDownloadResult> DownloadAsync(DepotDownloadRequest request,
        IProgress<DepotProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            return DepotDownloadResult.Failed(request, "OutputPath must not be empty.");
        }

        if (!CDepotNatives.TryResolve(out string? bindError))
        {
            return DepotDownloadResult.Failed(request, bindError!);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunAsync(request, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<DepotDownloadResult> RunAsync(DepotDownloadRequest request,
        IProgress<DepotProgress>? progress, CancellationToken cancellationToken)
    {
        string outputPath = Path.GetFullPath(request.OutputPath);

        var log = InterfaceSystem.GetInterface<ILoggingSystem>(InterfaceNames.LoggingSystem);
        int channel = log?.FindChannel("Depot") ?? -1;
        var scrape = new Scrape(progress, log, channel);

        using var capture = CConsoleCapture.Install(scrape.OnLine);
        try
        {
            if (!_accountSettingsLoaded)
            {
                CDepotNatives.LoadAccountSettings(AccountSettingsFile);
                _accountSettingsLoaded = true;
            }

            CDepotNatives.ResetDepotConfigStore();
            ApplyConfig(request, outputPath);

            if (!CDepotNatives.InitializeSteam3(null, null))
            {
                return DepotDownloadResult.Failed(request, "Steam login failed (anonymous).");
            }

            try
            {
                ulong manifestId = request.ManifestId ?? IDepotDownloader.InvalidManifestId;
                List<(uint, ulong)> depots = [(request.DepotId, manifestId)];

                await CDepotNatives
                    .DownloadAppAsync(request.AppId, depots, request.Branch, null, null, null, false, false)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                CDepotNatives.ShutdownSteam3();
            }

            capture.Flush();

            return DepotDownloadResult.Ok(request,
                scrape.ManifestId ?? request.ManifestId ?? IDepotDownloader.InvalidManifestId,
                scrape.BytesDownloaded,
                scrape.FilesCompleted);
        }
        catch (OperationCanceledException)
        {
            return DepotDownloadResult.Failed(request, "Download cancelled.");
        }
        catch (Exception ex)
        {
            return DepotDownloadResult.Failed(request, (ex.InnerException ?? ex).Message);
        }
    }

    private static void ApplyConfig(DepotDownloadRequest request, string outputPath)
    {
        CDepotNatives.SetConfig("InstallDirectory", outputPath);
        CDepotNatives.SetConfig("CellID", 0);
        CDepotNatives.SetConfig("MaxDownloads", 8);
        CDepotNatives.SetConfig("VerifyAll", false);
        CDepotNatives.SetConfig("DownloadManifestOnly", false);
        CDepotNatives.SetConfig("RememberPassword", false);
        CDepotNatives.SetConfig("UseQrCode", false);
        CDepotNatives.SetConfig("SkipAppConfirmation", true);
        CDepotNatives.SetConfig("DownloadAllPlatforms", false);
        CDepotNatives.SetConfig("DownloadAllArchs", false);
        CDepotNatives.SetConfig("DownloadAllLanguages", false);
        CDepotNatives.SetConfig("BetaPassword", null);
        CDepotNatives.SetConfig("LoginID", null);

        var files = request.FileList;
        if (files == null || files.Count == 0)
        {
            CDepotNatives.SetConfig("UsingFileList", false);
            CDepotNatives.SetConfig("FilesToDownload", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            CDepotNatives.SetConfig("FilesToDownloadRegex", new List<Regex>());
            return;
        }

        var literals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var patterns = new List<Regex>();

        foreach (string entry in files)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            if (entry.StartsWith(IDepotDownloader.RegexPrefix, StringComparison.Ordinal))
            {
                patterns.Add(new Regex(entry[IDepotDownloader.RegexPrefix.Length..],
                    RegexOptions.IgnoreCase | RegexOptions.Compiled));
            }
            else
            {
                literals.Add(entry.Replace('\\', '/'));
            }
        }

        CDepotNatives.SetConfig("UsingFileList", true);
        CDepotNatives.SetConfig("FilesToDownload", literals);
        CDepotNatives.SetConfig("FilesToDownloadRegex", patterns);
    }

    private sealed partial class Scrape(IProgress<DepotProgress>? progress, ILoggingSystem? log, int channel)
    {
        internal ulong? ManifestId { get; private set; }
        internal ulong BytesDownloaded { get; private set; }
        internal uint FilesCompleted { get; private set; }

        internal void OnLine(string line)
        {
            var percent = PercentLine().Match(line);

            if (percent.Success)
            {
                log?.DetailedMsg(channel, line);
            }
            else
            {
                log?.Msg(channel, line);
            }

            if (percent.Success)
            {
                FilesCompleted++;
                double fraction = double.Parse(percent.Groups[1].Value, CultureInfo.InvariantCulture) / 100.0;
                progress?.Report(new DepotProgress(
                    Math.Clamp(fraction, 0.0, 1.0),
                    BytesDownloaded,
                    0,
                    FilesCompleted,
                    0,
                    percent.Groups[2].Value));
                return;
            }

            var manifest = ManifestLine().Match(line);
            if (manifest.Success && ulong.TryParse(manifest.Groups[1].Value, out ulong id))
            {
                ManifestId = id;
                return;
            }

            var total = TotalLine().Match(line);
            if (total.Success && ulong.TryParse(total.Groups[1].Value, out ulong bytes))
            {
                BytesDownloaded = bytes;
                progress?.Report(new DepotProgress(1.0, bytes, bytes, FilesCompleted, FilesCompleted, null));
            }
        }

        // "{0,6:#00.00}% {1}"
        [GeneratedRegex(@"^\s*(\d+(?:\.\d+)?)%\s+(.+)$")]
        private static partial Regex PercentLine();

        // "Manifest {0} ({1})"
        [GeneratedRegex(@"^Manifest (\d+) \(")]
        private static partial Regex ManifestLine();

        // "Total downloaded: {0} bytes ({1} bytes uncompressed) from {2} depots"
        [GeneratedRegex(@"^Total downloaded: (\d+) bytes")]
        private static partial Regex TotalLine();
    }
}
