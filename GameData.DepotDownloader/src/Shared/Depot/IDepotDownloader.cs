namespace GameData.DepotDownloader.Shared.Depot;

public interface IDepotDownloader
{
    const string DefaultBranch = "public";

    const string RegexPrefix = "regex:";

    const ulong InvalidManifestId = ulong.MaxValue;

    Task<DepotDownloadResult> DownloadAsync(DepotDownloadRequest request,
        IProgress<DepotProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
