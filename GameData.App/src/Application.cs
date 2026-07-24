using GameData.Tier0.Shared.CommandLine;
using GameData.Tier0.Shared.ConVar;
using GameData.Tier0.Shared.Interfaces;
using GameData.Tier0.Shared.Logging;
using GameData.Tier0.Shared.String;

public class Application
{
    private static readonly ConVar<int> s_earlyConVar =
        new("early_convar", 7, description: "constructed before Tier0 loads");

    public static void Main(string[] args)
    {
        InterfaceSystem.LoadModule("GameData.Tier0");

        var strConv = InterfaceSystem.GetInterface<IStrConv>(InterfaceNames.StrConv)!;
        var cmd = InterfaceSystem.GetInterface<ICommandLine>(InterfaceNames.CommandLine)!;
        var log = InterfaceSystem.GetInterface<ILoggingSystem>(InterfaceNames.LoggingSystem)!;
        var cvars = InterfaceSystem.GetInterface<IConVarSystem>(InterfaceNames.ConVar)!;

        cmd.Initialize(args);

        int general = log.FindChannel("General");

        cvars.ConVarChanged += cv => log.Msg(general, $"[convar] {cv.Name} = {cv.ToStringValue()}");

        log.Msg(general, $"early_convar drained from queue: value={s_earlyConVar.Value}, known={cvars.Exists("early_convar")}");

        var maxPlayers = new ConVar<int>("maxplayers", 32, description: "max players", bounds: (1, 64));
        maxPlayers.OnChanged += (cv, old, now) => log.Msg(general, $"maxplayers {old} -> {now}");

        maxPlayers.Value = 24;
        maxPlayers.Value = 100;
        log.Msg(general, $"maxplayers clamped value: {maxPlayers.Value}");

        maxPlayers.SetFromString("48");
        log.Msg(general, $"maxplayers via SetFromString: {maxPlayers.ToStringValue()}");

        var hostname = new ConVar<string>("hostname", "dedicated");
        hostname.SetFromString("my server");
        log.Msg(general, $"hostname: {hostname.ToStringValue()}");

        var god = new ConVar<bool>("sv_god", false, flags: ConVarFlags.ReadOnly);
        try
        {
            god.Value = true;
        }
        catch (InvalidOperationException ex)
        {
            log.Warning(general, ex.Message);
        }

        try
        {
            _ = new ConVar<decimal>("bad_convar", 0m);
        }
        catch (NotSupportedException ex)
        {
            log.Warning(general, ex.Message);
        }

        log.Msg(general, $"total convars: {cvars.Count}");

        InterfaceSystem.RemoveAll();
    }
}
