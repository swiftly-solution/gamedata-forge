using System.Text;
using System.Text.RegularExpressions;

namespace GameData.DepotDownloader.Core.Depot;

internal sealed partial class CConsoleCapture : TextWriter, IDisposable
{
    private readonly Action<string> _onLine;
    private readonly TextWriter _previous;
    private readonly StringBuilder _pending = new();
    private readonly Lock _lock = new();
    private bool _disposed;

    private CConsoleCapture(Action<string> onLine, TextWriter previous)
    {
        _onLine = onLine;
        _previous = previous;
    }

    internal static CConsoleCapture Install(Action<string> onLine)
    {
        var previous = Console.Out;
        var capture = new CConsoleCapture(onLine, previous);
        Console.SetOut(capture);
        return capture;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        string? line = null;

        lock (_lock)
        {
            // Progress lines are rewritten in place with '\r', so treat both
            // terminators as an end of line.
            if (value is '\n' or '\r')
            {
                if (_pending.Length > 0)
                {
                    line = _pending.ToString();
                    _pending.Clear();
                }
            }
            else
            {
                _pending.Append(value);
            }
        }

        if (line != null)
        {
            Emit(line);
        }
    }

    public override void Write(string? value)
    {
        if (value == null)
        {
            return;
        }

        foreach (char c in value)
        {
            Write(c);
        }
    }

    private void Emit(string raw)
    {
        string line = Escapes().Replace(raw, string.Empty).Trim();
        if (line.Length == 0)
        {
            return;
        }

        _onLine(line);
    }

    public override void Flush()
    {
        string? line = null;

        lock (_lock)
        {
            if (_pending.Length > 0)
            {
                line = _pending.ToString();
                _pending.Clear();
            }
        }

        if (line != null)
        {
            Emit(line);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            Flush();
            Console.SetOut(_previous);
        }

        base.Dispose(disposing);
    }

    // CSI sequences plus the OSC progress reports DepotDownloader's Ansi helper emits.
    [GeneratedRegex(@"\x1b\[[0-9;?]*[a-zA-Z]|\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)")]
    private static partial Regex Escapes();
}
