namespace GameData.DepotDownloader.Shared.Depot;

public sealed record DepotDownloadResult
{
    public required bool Success { get; init; }

    public required uint AppId { get; init; }

    public required uint DepotId { get; init; }

    public required ulong ManifestId { get; init; }

    public required string OutputPath { get; init; }

    public ulong BytesDownloaded { get; init; }

    public uint FilesDownloaded { get; init; }

    public string? Error { get; init; }

    public static DepotDownloadResult Ok(DepotDownloadRequest request, ulong manifestId,
        ulong bytesDownloaded, uint filesDownloaded) => new()
        {
            Success = true,
            AppId = request.AppId,
            DepotId = request.DepotId,
            ManifestId = manifestId,
            OutputPath = request.OutputPath,
            BytesDownloaded = bytesDownloaded,
            FilesDownloaded = filesDownloaded,
        };

    public static DepotDownloadResult Failed(DepotDownloadRequest request, string error) => new()
    {
        Success = false,
        AppId = request.AppId,
        DepotId = request.DepotId,
        ManifestId = request.ManifestId ?? IDepotDownloader.InvalidManifestId,
        OutputPath = request.OutputPath,
        Error = error,
    };
}
