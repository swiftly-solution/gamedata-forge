using System.Diagnostics;
using GameData.IDA.Core.Native;
using GameData.IDA.Shared.Ida;
using GameData.IDA.Shared.Interfaces;
using GameData.Tier0.Shared.Interfaces;

namespace GameData.IDA.Core.Kernel;

[ExposeInterface(IdaInterfaceNames.IdaLibrary)]
internal sealed unsafe class CIdaLibrary : IIdaLibrary
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(30);

    public bool IsInitialized => CIdaNative.IsInitialized;

    public bool IsDatabaseOpen => DatabasePath != null;

    public string? DatabasePath { get; private set; }

    public IdaSdkVersion SdkVersion => CIdaNative.SdkVersion;

    public bool TryInitialize(out string? error)
        => CIdaNative.TryInitialize(CIdaConVars.IdaPath, CIdaConVars.IdaSdk, out error);

    public IdaVersion GetVersion()
    {
        CIdaNative.AssertOwnerThread();

        int major = 0;
        int minor = 0;
        int build = 0;

        return Ida.get_library_version(&major, &minor, &build) != 0
            ? new IdaVersion(major, minor, build)
            : default;
    }

    public void SetConsoleMessages(bool enabled)
    {
        CIdaNative.AssertOwnerThread();
        Ida.enable_console_messages(enabled ? (byte)1 : (byte)0);
    }

    public int Open(string path, bool runAutoAnalysis = true, Action<IdaAnalysisProgress>? onProgress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        CIdaNative.AssertOwnerThread();

        if (IsDatabaseOpen)
        {
            throw new InvalidOperationException(
                $"A database is already open ('{DatabasePath}'); close it before opening another.");
        }

        string full = Path.GetFullPath(path);
        byte* native = Utf8.Allocate(full);

        bool driveAnalysisHere = runAutoAnalysis && onProgress != null;

        try
        {
            byte runAuto = runAutoAnalysis && !driveAnalysisHere ? (byte)1 : (byte)0;

            int status = Ida.open_database(native, runAuto, null);
            if (status != 0)
            {
                return status;
            }

            DatabasePath = full;
        }
        finally
        {
            Utf8.Free(native);
        }

        if (driveAnalysisHere)
        {
            RunAnalysis(onProgress!);
        }

        return 0;
    }

    private static void RunAnalysis(Action<IdaAnalysisProgress> onProgress)
    {
        ulong start = Ida.getinf(IdaAbi.Current.MinEa);
        ulong end = Ida.getinf(IdaAbi.Current.MaxEa);
        ulong span = end > start ? end - start : 0;

        var display = new auto_display_t();
        var clock = Stopwatch.StartNew();
        TimeSpan nextReport = TimeSpan.Zero;
        double reported = 0.0;

        onProgress(new IdaAnalysisProgress(start, start, end, 0.0));

        while (Ida.auto_make_step(start, end) != 0)
        {
            if (clock.Elapsed < nextReport)
            {
                continue;
            }

            nextReport = clock.Elapsed + ReportInterval;

            if (span == 0 || Ida.get_auto_display(&display) == 0)
            {
                continue;
            }

            ulong address = display.Ea;
            if (address < start || address >= end)
            {
                continue;
            }

            double fraction = (double)(address - start) / span;
            if (fraction <= reported)
            {
                continue;
            }

            reported = fraction;
            onProgress(new IdaAnalysisProgress(address, start, end, fraction));
        }

        Ida.auto_wait();

        onProgress(new IdaAnalysisProgress(end, start, end, 1.0));
    }

    public void Close(bool save = false)
    {
        if (!IsDatabaseOpen)
        {
            return;
        }

        CIdaNative.AssertOwnerThread();
        Ida.close_database(save ? (byte)1 : (byte)0);
        DatabasePath = null;
    }

    public bool MakeSignatures(bool onlyPattern = false)
    {
        RequireDatabase();
        return Ida.make_signatures(onlyPattern ? (byte)1 : (byte)0) != 0;
    }

    public int FunctionCount
    {
        get
        {
            RequireDatabase();
            return (int)Ida.get_func_qty();
        }
    }

    public int SegmentCount
    {
        get
        {
            RequireDatabase();
            return Ida.get_segm_qty();
        }
    }

    public IReadOnlyList<IdaFunction> GetFunctions()
    {
        RequireDatabase();

        nuint count = Ida.get_func_qty();
        var functions = new List<IdaFunction>((int)count);

        for (nuint i = 0; i < count; i++)
        {
            func_t* function = Ida.getn_func(i);
            if (function == null)
            {
                continue;
            }

            var range = *(range_t*)function;
            functions.Add(new IdaFunction(range.StartEa, range.EndEa, ReadFunctionName(range.StartEa)));
        }

        return functions;
    }

    public IReadOnlyList<IdaSegment> GetSegments()
    {
        RequireDatabase();

        int count = Ida.get_segm_qty();
        var segments = new List<IdaSegment>(count);

        for (int i = 0; i < count; i++)
        {
            segment_t* segment = Ida.getnseg(i);
            if (segment == null)
            {
                continue;
            }

            var range = *(range_t*)segment;

            using var name = new QStringBuffer();
            using var sclass = new QStringBuffer();

            Ida.get_segm_name(name.Pointer, segment, 0);
            Ida.get_segm_class(sclass.Pointer, segment);

            segments.Add(new IdaSegment(range.StartEa, range.EndEa, name.ToString(), sclass.ToString()));
        }

        return segments;
    }

    public string? GetName(ulong address)
    {
        RequireDatabase();

        using var buffer = new QStringBuffer();
        nint length = Ida.get_ea_name(buffer.Pointer, address, 0, null);
        return length > 0 ? buffer.ToString() : null;
    }

    public IdaFunction? GetFunctionAt(ulong address)
    {
        RequireDatabase();

        func_t* function = Ida.get_func(address);
        if (function == null)
        {
            return null;
        }

        var range = *(range_t*)function;
        return new IdaFunction(range.StartEa, range.EndEa, ReadFunctionName(range.StartEa));
    }

    public byte[] ReadBytes(ulong address, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        RequireDatabase();

        if (count == 0)
        {
            return [];
        }

        var buffer = new byte[count];
        nint read;

        fixed (byte* destination = buffer)
        {
            read = Ida.get_bytes(destination, count, address, IdaConstants.GetBytes.ReadAll, null);
        }

        if (read <= 0)
        {
            return [];
        }

        return read == count ? buffer : buffer[..(int)read];
    }

    public ulong? FindBinary(string pattern, ulong start = 0, ulong end = ulong.MaxValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        RequireDatabase();

        byte* native = Utf8.Allocate(pattern);
        using var compiled = new CompiledPatternVector();

        try
        {
            using var errors = new QStringBuffer();

            if (Ida.parse_binpat_str(compiled.Pointer, start, native, 16, 0, errors.Pointer) == 0)
            {
                string message = errors.ToString();
                throw new ArgumentException(
                    message.Length > 0 ? message : $"'{pattern}' is not a valid IDA byte pattern.",
                    nameof(pattern));
            }

            if (compiled.IsEmpty)
            {
                throw new ArgumentException($"'{pattern}' compiled to an empty pattern.", nameof(pattern));
            }

            const int flags = IdaConstants.BinSearch.Forward
                            | IdaConstants.BinSearch.Case
                            | IdaConstants.BinSearch.NoBreak
                            | IdaConstants.BinSearch.NoShow;

            ulong found = Ida.bin_search(start, end, compiled.Pointer, flags, null);
            return found == IdaConstants.BadAddress ? null : found;
        }
        finally
        {
            Utf8.Free(native);
        }
    }

    private static string ReadFunctionName(ulong start)
    {
        using var buffer = new QStringBuffer();
        return Ida.get_func_name(buffer.Pointer, start) > 0 ? buffer.ToString() : string.Empty;
    }

    private void RequireDatabase()
    {
        CIdaNative.AssertOwnerThread();

        if (!IsDatabaseOpen)
        {
            throw new InvalidOperationException("No database is open; call Open() first.");
        }
    }
}
