namespace GameData.IDA.Shared.Ida;

public sealed record IdaBatchItem(
    string Path,
    bool Succeeded,
    int Functions,
    int Segments,
    TimeSpan Elapsed,
    string? Error);

public sealed record IdaWorkerStatus(
    int Index,
    bool Alive,
    string? Sdk,
    string? Version,
    string? CurrentPath);

public sealed record IdaBatchProgress(
    string Path,
    int Worker,
    double Fraction,
    string? Address,
    bool Finished,
    IdaBatchItem? Item);

public interface IIdaPool
{
    int Size { get; }

    bool IsRunning { get; }

    IReadOnlyList<IdaWorkerStatus> GetStatus();

    IReadOnlyList<IdaBatchItem> RunBatch(
        IReadOnlyList<string> paths,
        bool save = true,
        Action<IdaBatchProgress>? onProgress = null);

    void Stop();
}
