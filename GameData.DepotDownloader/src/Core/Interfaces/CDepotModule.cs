using GameData.Tier0.Shared.Drawing;
using GameData.Tier0.Shared.Interfaces;
using GameData.Tier0.Shared.Logging;

namespace GameData.DepotDownloader.Core.Interfaces;

internal sealed class CDepotModule : IModule
{
    public void Init(IInterfaceSystem system)
    {
        var logging = system.GetInterface<ILoggingSystem>(InterfaceNames.LoggingSystem);
        logging?.RegisterChannel("Depot", color: new Color(120, 180, 255));

        Terminal.CDepotCommands.Register();
    }

    public void Shutdown()
    {
    }
}
