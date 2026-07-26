using GameData.IDA.Shared.Ida;
using GameData.Tier0.Shared.CommandLine;
using GameData.Tier0.Shared.Interfaces;
using GameData.Tier0.Shared.Terminal;

public class Application
{
    public static int Main(string[] args)
    {
        InterfaceSystem.LoadModule("GameData.Tier0");
        InterfaceSystem.LoadModule("GameData.DepotDownloader");
        InterfaceSystem.LoadModule("GameData.IDA");

        var cmd = InterfaceSystem.GetInterface<ICommandLine>(InterfaceNames.CommandLine)!;
        var terminal = InterfaceSystem.GetInterface<ITerminal>(InterfaceNames.Terminal)!;

        cmd.Initialize(args);

        int code = IdaWorkerHost.IsWorker ? IdaWorkerHost.Run() : Interactive(terminal);

        InterfaceSystem.RemoveAll();
        return code;
    }

    private static int Interactive(ITerminal terminal)
    {
        terminal.Run();
        return 0;
    }
}
