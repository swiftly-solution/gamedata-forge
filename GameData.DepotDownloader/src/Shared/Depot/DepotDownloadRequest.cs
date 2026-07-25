namespace GameData.DepotDownloader.Shared.Depot;

public sealed record DepotDownloadRequest
{
    public required uint AppId { get; init; }

    public required uint DepotId { get; init; }

    public required string OutputPath { get; init; }

    public string Branch { get; init; } = IDepotDownloader.DefaultBranch;

    // Depot-relative paths to download; entries prefixed with IDepotDownloader.RegexPrefix
    // are matched as regex. Null or empty downloads the whole depot.
    public IReadOnlyList<string>? FileList { get; init; }

    // Null pins the branch's current manifest.
    public ulong? ManifestId { get; init; }
}
