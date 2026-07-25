namespace GameData.DepotDownloader.Shared.Depot;

public readonly record struct DepotProgress(
    double Fraction,
    ulong BytesDownloaded,
    ulong TotalBytes,
    uint FilesCompleted,
    uint TotalFiles,
    string? CurrentFile);
