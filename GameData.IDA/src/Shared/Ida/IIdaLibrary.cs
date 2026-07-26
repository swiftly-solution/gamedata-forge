namespace GameData.IDA.Shared.Ida;

public interface IIdaLibrary
{
    bool IsInitialized { get; }

    bool IsDatabaseOpen { get; }

    string? DatabasePath { get; }

    bool TryInitialize(out string? error);

    IdaVersion GetVersion();

    IdaSdkVersion SdkVersion { get; }

    void SetConsoleMessages(bool enabled);

    int Open(string path, bool runAutoAnalysis = true, Action<IdaAnalysisProgress>? onProgress = null);

    int FunctionCount { get; }

    int SegmentCount { get; }

    int StringCount { get; }

    void Close(bool save = false);

    bool MakeSignatures(bool onlyPattern = false);

    IReadOnlyList<IdaFunction> GetFunctions();

    IReadOnlyList<IdaSegment> GetSegments();

    string? GetName(ulong address);

    IdaFunction? GetFunctionAt(ulong address);

    byte[] ReadBytes(ulong address, int count);

    ulong? FindBinary(string pattern, ulong start = 0, ulong end = ulong.MaxValue);
}
