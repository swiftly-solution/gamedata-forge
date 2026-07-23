using GameData.Tier0.Shared.CommandLine;
using GameData.Tier0.Shared.Interfaces;
using GameData.Tier0.Shared.String;

public class Application
{
    public static void Main(string[] args)
    {
        InterfaceSystem.LoadModule("GameData.Tier0");

        var strConv = InterfaceSystem.GetInterface<IStrConv>(InterfaceNames.StrConv)!;
        var cmd = InterfaceSystem.GetInterface<ICommandLine>(InterfaceNames.CommandLine)!;

        cmd.Initialize(args);

        InterfaceSystem.RemoveAll();
    }
}
