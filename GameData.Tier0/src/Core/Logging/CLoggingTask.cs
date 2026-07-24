using GameData.Tier0.Shared.Logging;

namespace GameData.Tier0.Core.Logging;

internal sealed class CLoggingTask : ILoggingTask
{
    private readonly CLoggingSystem _owner;
    private double? _progress;
    private bool _running = true;

    public CLoggingTask(CLoggingSystem owner, int channelId, string label, double? progress)
    {
        _owner = owner;
        ChannelId = channelId;
        Label = label;
        _progress = progress;
    }

    public int ChannelId { get; }

    public string Label { get; set; }

    public double? Progress
    {
        get => _progress;
        set => _progress = value is double d ? Math.Clamp(d, 0.0, 1.0) : null;
    }

    public bool IsRunning => _running;

    public void Report(double progress) => Progress = progress;

    public void Report(double progress, string label)
    {
        Progress = progress;
        Label = label;
    }

    public void Complete(string? message = null) => Finish(true, message);

    public void Fail(string? message = null) => Finish(false, message);

    public void Dispose()
    {
        if (_running)
        {
            Finish(true, null);
        }
    }

    private void Finish(bool ok, string? message)
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _owner.EndTask(this, ok, message);
    }
}
