using GameData.IDA.Core.Kernel;
using GameData.IDA.Core.Pool;

namespace GameData.IDA.Shared.Ida;

public static class IdaWorkerHost
{
    public static bool IsWorker => CIdaConVars.IsWorker;

    public static int Run() => CIdaWorker.Run();
}
