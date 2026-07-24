using GameData.Tier0.Shared.ConVar;
using GameData.Tier0.Shared.Drawing;
using GameData.Tier0.Shared.Interfaces;
using GameData.Tier0.Shared.Logging;
using GameData.Tier0.Shared.Terminal;

namespace GameData.Tier0.Core.Interfaces;

internal sealed class CTier0Module : IModule
{
    public void Init(IInterfaceSystem system)
    {
        system.GetInterface<IConVarSystem>(InterfaceNames.ConVar);
        system.GetInterface<ITerminal>(InterfaceNames.Terminal);
        Terminal.CTerminalCommands.Register();

        var logging = system.GetInterface<ILoggingSystem>(InterfaceNames.LoggingSystem);
        if (logging == null)
        {
            return;
        }

        logging.RegisterListener(new Logging.CSpectreLoggingListener());

        logging.RegisterChannel("General", color: new Color(220, 220, 220));
        logging.RegisterChannel("Console", LoggingChannelFlags.ConsoleOnly, color: new Color(150, 150, 150));
        logging.RegisterChannel("Developer", color: new Color(0, 200, 200));
    }

    public void Shutdown()
    {
    }
}
