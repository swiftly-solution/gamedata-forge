using GameData.Tier0.Shared.Drawing;
using GameData.Tier0.Shared.Interfaces;
using GameData.Tier0.Shared.Logging;

namespace GameData.Tier0.Core.Interfaces;

internal sealed class CTier0Module : IModule
{
    public void Init(IInterfaceSystem system)
    {
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
