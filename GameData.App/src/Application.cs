using GameData.Tier0.Shared.CommandLine;
using GameData.Tier0.Shared.Interfaces;
using GameData.Tier0.Shared.Terminal;

public class Application
{
    public static void Main(string[] args)
    {
        InterfaceSystem.LoadModule("GameData.Tier0");

        var cmd = InterfaceSystem.GetInterface<ICommandLine>(InterfaceNames.CommandLine)!;
        var terminal = InterfaceSystem.GetInterface<ITerminal>(InterfaceNames.Terminal)!;

        cmd.Initialize(args);

        terminal.Run();

        InterfaceSystem.RemoveAll();
    }
}
