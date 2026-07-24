using GameData.Tier0.Shared.ConVar;
using GameData.Tier0.Shared.Logging;

namespace GameData.Tier0.Core.Logging;

internal sealed class CLoggingConVars : ILoggingListener
{
    private readonly ILoggingSystem _log;

    public CLoggingConVars(ILoggingSystem log)
    {
        _log = log;
    }

    public void Log(LoggingContext context, string message)
    {
    }

    public void OnChannelRegistered(int channelId)
    {
        string? channelName = _log.GetChannelName(channelId);
        if (channelName == null)
        {
            return;
        }

        var convar = new ConVar<int>(
            $"log_{channelName.ToLowerInvariant()}",
            (int)_log.GetChannelVerbosity(channelId),
            $"Verbosity of the '{channelName}' log channel (0=off .. 4=max).",
            bounds: (0, (int)LoggingVerbosity.Max));

        convar.OnChanged += (_, _, value) => _log.SetChannelVerbosity(channelId, (LoggingVerbosity)value);
    }

    public void CreateGlobalConVar()
    {
        var convar = new ConVar<int>(
            "log_level",
            (int)LoggingVerbosity.Default,
            "Sets the verbosity of all log channels (0=off .. 4=max).",
            bounds: (0, (int)LoggingVerbosity.Max));

        convar.OnChanged += (_, _, value) =>
        {
            for (int id = 0; id < _log.ChannelCount; id++)
            {
                _log.SetChannelVerbosity(id, (LoggingVerbosity)value);
            }
        };
    }
}
