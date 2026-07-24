using GameData.Tier0.Shared.CommandLine;
using GameData.Tier0.Shared.Interfaces;
using GameData.Tier0.Shared.Logging;

namespace GameData.Tier0.Core.Logging;

internal sealed class CDefaultLoggingResponsePolicy : ILoggingResponsePolicy
{
    public LoggingResponse OnLog(LoggingContext context)
    {
        if (context.Severity == LoggingSeverity.Assert && !HasNoAssert())
        {
            return LoggingResponse.Debugger;
        }

        if (context.Severity == LoggingSeverity.Error)
        {
            return LoggingResponse.Abort;
        }

        return LoggingResponse.Continue;
    }

    private static bool HasNoAssert()
        => InterfaceSystem.GetInterface<ICommandLine>(InterfaceNames.CommandLine)?.HasParameter("noassert") ?? false;
}
