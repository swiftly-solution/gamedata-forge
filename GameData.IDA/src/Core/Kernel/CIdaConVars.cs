using GameData.IDA.Shared.Ida;
using GameData.Tier0.Shared.ConVar;

namespace GameData.IDA.Core.Kernel;

internal static class CIdaConVars
{
    private static ConVar<string>? _idaPath;
    private static ConVar<IdaSdkVersion>? _idaSdk;
    private static ConVar<int>? _cores;
    private static ConVar<bool>? _idaWorker;
    private static ConVar<bool>? _pltPatch;

    internal static string IdaPath => _idaPath?.Value ?? string.Empty;

    internal static IdaSdkVersion IdaSdk => _idaSdk?.Value ?? IdaSdkVersion.Auto;

    internal static int Cores => _cores?.Value ?? 1;

    internal static bool IsWorker => _idaWorker?.Value ?? false;

    internal static bool PltPatch => _pltPatch?.Value ?? true;

    internal static void Register()
    {
        _idaPath ??= new ConVar<string>(
            "ida_path",
            string.Empty,
            "Directory of the IDA installation to load the kernel and idalib libraries from. " +
            "Empty uses the nearest 'binary' directory at or above the application. " +
            "Read-only: set it at startup with -ida_path <dir>.", ConVarFlags.ReadOnly);

        _idaSdk ??= new ConVar<IdaSdkVersion>(
            "ida_sdk",
            IdaSdkVersion.Auto,
            "Which generated bindings to resolve the IDA libraries against (Auto, V92, V93). " +
            "Auto detects the installed version and refuses if this build has no bindings for it. " +
            "Read-only: set it at startup with -ida_sdk <version>.", ConVarFlags.ReadOnly);

        _cores ??= new ConVar<int>(
            "cores",
            Math.Min(Environment.ProcessorCount, 1),
            "How many binaries 'ida_batch' analyzes at once, one worker process per core. Each " +
            "worker is a full IDA kernel with its own database, so this is a memory budget as much " +
            "as a CPU one. Read-only: set it at startup with -cores <n>.",
            ConVarFlags.ReadOnly,
            (1, 64));

        _idaWorker ??= new ConVar<bool>(
            "ida_worker",
            false,
            "Run as an analysis worker driven over stdin instead of as the interactive terminal. " +
            "Set by the pool on the processes it spawns; there is no reason to set it by hand " +
            "except to test the worker protocol. Read-only: set it at startup with -ida_worker 1.",
            ConVarFlags.ReadOnly);

        _pltPatch ??= new ConVar<bool>(
            "ida_plt_patch",
            true,
            "Repair PLT stubs after analyzing an ELF64, for the binaries IDA gives up on with " +
            "'Could not patch the PLT stub' — mold-linked ones, mostly. Without it, calls to " +
            "imported functions have no cross-references. Non-ELF64 inputs ignore this.");
    }
}
