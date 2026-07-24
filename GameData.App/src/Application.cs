using GameData.Tier0.Shared.CommandLine;
using GameData.Tier0.Shared.Interfaces;
using GameData.Tier0.Shared.Logging;
using GameData.Tier0.Shared.String;

public class Application
{
    public static void Main(string[] args)
    {
        InterfaceSystem.LoadModule("GameData.Tier0");

        var strConv = InterfaceSystem.GetInterface<IStrConv>(InterfaceNames.StrConv)!;
        var cmd = InterfaceSystem.GetInterface<ICommandLine>(InterfaceNames.CommandLine)!;
        var log = InterfaceSystem.GetInterface<ILoggingSystem>(InterfaceNames.LoggingSystem)!;

        cmd.Initialize(args);

        int general = log.FindChannel("General");
        int developer = log.FindChannel("Developer");

        log.Msg(general, "Logging system online.");
        log.Warning(general, "This is a warning.");
        log.DetailedMsg(general, "You will not see this at default verbosity.");

        log.SetChannelVerbosity(developer, LoggingVerbosity.Detailed);
        log.Msg(developer, "Developer channel at Detailed verbosity.");

        log.SetChannelVerbosity(developer, LoggingVerbosity.Max);
        log.DetailedMsg(developer, "Developer channel at Max verbosity.");

        log.Msg(general, "Brackets are safe: [not-a-tag] value=[42]");

        InterfaceSystem.RemoveAll();
    }
}
