using GameData.IDA.Shared.Ida;
using GameData.IDA.Shared.Interfaces;
using GameData.Tier0.Shared.Drawing;
using GameData.Tier0.Shared.Interfaces;
using GameData.Tier0.Shared.Logging;

namespace GameData.IDA.Core.Interfaces;

internal sealed class CIdaModule : IModule
{
    public void Init(IInterfaceSystem system)
    {
        var logging = system.GetInterface<ILoggingSystem>(InterfaceNames.LoggingSystem);
        logging?.RegisterChannel("IDA", color: new Color(255, 170, 90));

        Kernel.CIdaConVars.Register();
        Terminal.CIdaCommands.Register();
    }

    public void Shutdown()
    {
        InterfaceSystem.GetInterface<IIdaPool>(IdaInterfaceNames.IdaPool)?.Stop();

        InterfaceSystem.GetInterface<IIdaLibrary>(IdaInterfaceNames.IdaLibrary)?.Close(save: false);
    }
}
