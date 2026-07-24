using System.Text;
using GameData.Tier0.Shared.ConVar;
using GameData.Tier0.Shared.Interfaces;
using GameData.Tier0.Shared.Logging;
using GameData.Tier0.Shared.Terminal;
using Spectre.Console;

namespace GameData.Tier0.Core.Terminal;

[ExposeInterface(InterfaceNames.Terminal)]
internal sealed class CTerminal : ITerminal, ICommandSink
{
    private const string Csi = "\x1b[";
    private const int MaxScrollback = 2000;

    private readonly Lock _lock = new();
    private readonly Lock _render = new();
    private readonly Dictionary<string, IConCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _lines = [];
    private readonly StringBuilder _ansiBuffer = new();
    private readonly StringBuilder _frame = new();
    private readonly IAnsiConsole _ansi;
    private bool _running;
    private string _input = string.Empty;
    private int _cursorRow = 1;
    private int _cursorCol = 1;

    private ILoggingSystem? _log;
    private IConVarSystem? _convars;
    private int _channel = -1;

    public CTerminal()
    {
        _ansi = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Out = new AnsiConsoleOutput(new StringWriter(_ansiBuffer)),
        });
        _ansi.Profile.Width = 100000;
        _ansi.Profile.Height = 100000;

        CommandRegistry.Attach(this);
    }

    public bool IsRunning => _running;

    public void Index(IConCommand command)
    {
        lock (_lock)
        {
            if (!_commands.TryAdd(command.Name, command))
            {
                throw new InvalidOperationException($"A command named '{command.Name}' is already registered.");
            }
        }
    }

    public IConCommand? FindCommand(string name)
    {
        lock (_lock)
        {
            return _commands.GetValueOrDefault(name);
        }
    }

    public IEnumerable<IConCommand> GetCommands()
    {
        lock (_lock)
        {
            return [.. _commands.Values];
        }
    }

    public void Execute(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var tokens = Tokenize(line);
        if (tokens.Count == 0)
        {
            return;
        }

        string name = tokens[0];
        string[] args = [.. tokens.Skip(1)];
        string argString = string.Join(' ', args);

        var command = FindCommand(name);
        if (command != null)
        {
            command.Invoke(new CommandContext
            {
                Name = name,
                Args = args,
                ArgString = argString,
                Terminal = this,
                Print = Print,
                Warn = Warn,
            });
            return;
        }

        var convar = ConVars?.Find(name);
        if (convar != null)
        {
            if (args.Length == 0)
            {
                Print($"{convar.Name} = {convar.ToStringValue()}");
            }
            else
            {
                convar.SetFromString(argString);
            }
            return;
        }

        Warn($"Unknown command or convar: '{name}'");
    }

    public void Run()
    {
        if (System.Console.IsInputRedirected)
        {
            RunRedirected();
        }
        else
        {
            RunLive();
        }
    }

    public void Stop() => _running = false;

    private void RunRedirected()
    {
        _running = true;

        while (_running)
        {
            string? line = System.Console.ReadLine();
            if (line == null)
            {
                break;
            }

            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                Execute(line);
            }
            catch (Exception ex)
            {
                Warn(ex.Message);
            }
        }
    }

    private void RunLive()
    {
        _running = true;

        var previousEncoding = System.Console.OutputEncoding;
        System.Console.OutputEncoding = Encoding.UTF8;

        // Alternate screen buffer, autowrap off (so long lines don't break the layout).
        System.Console.Write($"{Csi}?1049h{Csi}?7l");
        TerminalOutput.Writer = WriteLine;

        try
        {
            lock (_render)
            {
                Render(includeLog: true);
            }

            while (_running)
            {
                var key = System.Console.ReadKey(intercept: true);
                string? submitted = null;

                lock (_render)
                {
                    switch (key.Key)
                    {
                        case ConsoleKey.Enter:
                            AppendLine($"[green]>[/] {Markup.Escape(_input)}");
                            submitted = _input;
                            _input = string.Empty;
                            Render(includeLog: true);
                            break;

                        case ConsoleKey.Backspace:
                            if (_input.Length > 0)
                            {
                                _input = _input[..^1];
                                Render(includeLog: false);
                            }
                            break;

                        default:
                            if (!char.IsControl(key.KeyChar))
                            {
                                _input += key.KeyChar;
                                Render(includeLog: false);
                            }
                            break;
                    }
                }

                if (submitted is { Length: > 0 })
                {
                    try
                    {
                        Execute(submitted);
                    }
                    catch (Exception ex)
                    {
                        Warn(ex.Message);
                    }
                }
            }
        }
        finally
        {
            TerminalOutput.Writer = null;
            System.Console.Write($"{Csi}?7h{Csi}?25h{Csi}?1049l");
            System.Console.OutputEncoding = previousEncoding;
        }
    }

    private void WriteLine(string markup)
    {
        lock (_render)
        {
            AppendLine(markup);
            Render(includeLog: true);
        }
    }

    private void AppendLine(string markup)
    {
        _ansiBuffer.Clear();
        _ansi.Markup(markup);
        _lines.Add(_ansiBuffer.ToString());

        if (_lines.Count > MaxScrollback)
        {
            _lines.RemoveRange(0, _lines.Count - MaxScrollback);
        }
    }

    private void Render(bool includeLog)
    {
        int height = System.Console.WindowHeight;
        int width = System.Console.WindowWidth;

        _frame.Clear();
        _frame.Append($"{Csi}?25l");

        if (includeLog)
        {
            BuildLog(height);
        }

        BuildInputBox(height, width);

        _frame.Append($"{Csi}{_cursorRow};{_cursorCol}H{Csi}?25h");

        System.Console.Out.Write(_frame);
    }

    private void BuildLog(int height)
    {
        int logRows = Math.Max(0, height - 3);
        int start = Math.Max(0, _lines.Count - logRows);

        for (int row = 0; row < logRows; row++)
        {
            _frame.Append($"{Csi}{row + 1};1H{Csi}2K");

            int index = start + row;
            if (index < _lines.Count)
            {
                _frame.Append(_lines[index]);
            }
        }
    }

    private void BuildInputBox(int height, int width)
    {
        int inner = Math.Max(0, width - 4);
        int top = height - 2;
        int mid = height - 1;
        int bottom = height;

        string border = new('─', Math.Max(0, width - 2));

        _frame.Append($"{Csi}{top};1H{Csi}2K┌{border}┐");
        _frame.Append($"{Csi}{bottom};1H{Csi}2K└{border}┘");

        string content = "> " + _input;
        string shown = content.Length > inner ? content[^inner..] : content;

        _frame.Append($"{Csi}{mid};1H{Csi}2K│ {shown.PadRight(inner)} │");

        _cursorRow = mid;
        _cursorCol = Math.Min(3 + shown.Length, Math.Max(3, width - 1));
    }

    private void Print(string message) => Log()?.Msg(Channel(), message);

    private void Warn(string message) => Log()?.Warning(Channel(), message);

    private ILoggingSystem? Log()
        => _log ??= InterfaceSystem.GetInterface<ILoggingSystem>(InterfaceNames.LoggingSystem);

    private IConVarSystem? ConVars
        => _convars ??= InterfaceSystem.GetInterface<IConVarSystem>(InterfaceNames.ConVar);

    private int Channel()
    {
        if (_channel < 0)
        {
            _channel = Log()?.FindChannel("Console") ?? -1;
        }
        return _channel;
    }

    private static List<string> Tokenize(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
