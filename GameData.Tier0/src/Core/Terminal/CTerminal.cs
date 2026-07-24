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
    private readonly record struct Suggestion(string Name, string? Description, bool IsCommand);

    private const string Csi = "\x1b[";
    private const int MaxScrollback = 2000;
    private const int MaxSuggestions = 8;

    private readonly Lock _lock = new();
    private readonly Lock _render = new();
    private readonly Dictionary<string, IConCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _lines = [];
    private readonly List<Suggestion> _suggestions = [];
    private readonly List<string> _history = [];
    private readonly StringBuilder _ansiBuffer = new();
    private readonly StringBuilder _frame = new();
    private readonly IAnsiConsole _ansi;
    private bool _running;
    private string _input = string.Empty;
    private int _suggestIndex = -1;
    private bool _inSuggestions;
    private int _historyIndex;
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
                Render();
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
                            if (_inSuggestions && _suggestIndex >= 0 && _suggestIndex < _suggestions.Count)
                            {
                                AcceptSuggestion(_suggestions[_suggestIndex].Name);
                                _inSuggestions = false;
                                Render();
                            }
                            else
                            {
                                AppendLine($"[green]>[/] {Markup.Escape(_input)}");
                                submitted = _input;
                                if (_input.Length > 0)
                                {
                                    _history.Add(_input);
                                }
                                _historyIndex = _history.Count;
                                _input = string.Empty;
                                ClearSuggestions();
                                _inSuggestions = false;
                                Render();
                            }
                            break;

                        case ConsoleKey.Backspace:
                            if (_input.Length > 0)
                            {
                                _input = _input[..^1];
                                _inSuggestions = false;
                                _historyIndex = _history.Count;
                                UpdateSuggestions();
                                Render();
                            }
                            break;

                        case ConsoleKey.Tab:
                            if (_suggestions.Count > 0)
                            {
                                if (!_inSuggestions)
                                {
                                    _inSuggestions = true;
                                    _suggestIndex = 0;
                                }
                                else
                                {
                                    _suggestIndex = (_suggestIndex + 1) % _suggestions.Count;
                                }
                                Render();
                            }
                            break;

                        case ConsoleKey.DownArrow:
                            if (_inSuggestions)
                            {
                                _suggestIndex = Math.Min(_suggestions.Count - 1, _suggestIndex + 1);
                            }
                            else
                            {
                                HistoryDown();
                            }
                            Render();
                            break;

                        case ConsoleKey.UpArrow:
                            if (_inSuggestions)
                            {
                                if (_suggestIndex <= 0)
                                {
                                    _inSuggestions = false;
                                    _suggestIndex = -1;
                                }
                                else
                                {
                                    _suggestIndex--;
                                }
                            }
                            else
                            {
                                HistoryUp();
                            }
                            Render();
                            break;

                        case ConsoleKey.Escape:
                            if (_inSuggestions)
                            {
                                _inSuggestions = false;
                                _suggestIndex = -1;
                                Render();
                            }
                            break;

                        default:
                            if (!char.IsControl(key.KeyChar))
                            {
                                _input += key.KeyChar;
                                _inSuggestions = false;
                                _historyIndex = _history.Count;
                                UpdateSuggestions();
                                Render();
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
            Render();
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

    private void Render()
    {
        int height = System.Console.WindowHeight;
        int width = System.Console.WindowWidth;

        int popupHeight = Math.Min(_suggestions.Count, MaxSuggestions);
        popupHeight = Math.Min(popupHeight, Math.Max(0, height - 5));

        _frame.Clear();
        _frame.Append($"{Csi}?25l");

        BuildLog(height, popupHeight);
        BuildSuggestions(height, width, popupHeight);
        BuildInputBox(height, width);
        BuildHint(height, width);

        _frame.Append($"{Csi}{_cursorRow};{_cursorCol}H{Csi}?25h");

        System.Console.Out.Write(_frame);
    }

    private void BuildLog(int height, int popupHeight)
    {
        int logRows = Math.Max(0, height - 4 - popupHeight);
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

    private void BuildSuggestions(int height, int width, int popupHeight)
    {
        if (popupHeight <= 0)
        {
            return;
        }

        int firstRow = height - 3 - popupHeight;

        int nameCol = 0;
        for (int i = 0; i < popupHeight; i++)
        {
            nameCol = Math.Max(nameCol, _suggestions[i].Name.Length);
        }
        nameCol = Math.Min(nameCol, 24);

        int descMax = Math.Max(0, width - 4 - nameCol);

        for (int i = 0; i < popupHeight; i++)
        {
            var suggestion = _suggestions[i];
            int row = firstRow + i;
            _frame.Append($"{Csi}{row};1H{Csi}2K");

            string name = suggestion.Name.Length > nameCol
                ? suggestion.Name[..nameCol]
                : suggestion.Name.PadRight(nameCol);

            string desc = suggestion.Description ?? "";
            if (desc.Length > descMax)
            {
                desc = descMax > 1 ? desc[..(descMax - 1)] + "…" : "";
            }

            bool selected = i == _suggestIndex;
            string marker = selected ? "▸ " : "  ";
            string nameColor = suggestion.IsCommand ? "38;5;81" : "38;5;222";

            if (selected)
            {
                string line = $"{marker}{name}  {desc}";
                if (line.Length > width)
                {
                    line = line[..width];
                }
                _frame.Append($"{Csi}48;5;238m{Csi}97m{line.PadRight(width)}{Csi}0m");
            }
            else
            {
                _frame.Append($"{marker}{Csi}{nameColor}m{name}{Csi}0m  {Csi}90m{desc}{Csi}0m");
            }
        }
    }

    private void BuildInputBox(int height, int width)
    {
        int inner = Math.Max(0, width - 4);
        int top = height - 3;
        int mid = height - 2;
        int bottom = height - 1;

        string border = new('─', Math.Max(0, width - 2));

        _frame.Append($"{Csi}{top};1H{Csi}2K┌{border}┐");
        _frame.Append($"{Csi}{bottom};1H{Csi}2K└{border}┘");

        string content = "> " + _input;
        string shown = content.Length > inner ? content[^inner..] : content;

        _frame.Append($"{Csi}{mid};1H{Csi}2K│ {shown.PadRight(inner)} │");

        _cursorRow = mid;
        _cursorCol = Math.Min(3 + shown.Length, Math.Max(3, width - 1));
    }

    private void BuildHint(int height, int width)
    {
        string hint = _inSuggestions
            ? "↑/↓ navigate   Enter select   Tab next   Esc cancel"
            : _suggestions.Count > 0
                ? "Tab suggestions   ↑/↓ history   Enter select"
                : "↑/↓ history   Enter select";

        string text = " " + hint;
        if (text.Length > width)
        {
            text = text[..width];
        }

        _frame.Append($"{Csi}{height};1H{Csi}2K{Csi}90m{text}{Csi}0m");
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

    private void HistoryUp()
    {
        if (_history.Count == 0)
        {
            return;
        }

        _historyIndex = Math.Max(0, _historyIndex - 1);
        _input = _history[_historyIndex];
        ClearSuggestions();
    }

    private void HistoryDown()
    {
        if (_history.Count == 0)
        {
            return;
        }

        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            _input = _history[_historyIndex];
        }
        else
        {
            _historyIndex = _history.Count;
            _input = string.Empty;
        }

        ClearSuggestions();
    }

    private void AcceptSuggestion(string suggestion)
    {
        string[] tokens = _input.Split(' ');
        tokens[^1] = suggestion;
        _input = string.Join(' ', tokens) + " ";
        UpdateSuggestions();
    }

    private void ClearSuggestions()
    {
        _suggestions.Clear();
        _suggestIndex = -1;
    }

    private void UpdateSuggestions()
    {
        ClearSuggestions();

        if (_input.Contains('"'))
        {
            return;
        }

        string[] tokens = _input.Split(' ');
        int index = tokens.Length - 1;
        string prefix = tokens[index];
        if (prefix.Length == 0)
        {
            return;
        }

        bool wantNames = index == 0
            || (index == 1 && tokens[0].Equals("help", StringComparison.OrdinalIgnoreCase));
        if (!wantNames)
        {
            return;
        }

        var items = new List<Suggestion>();

        foreach (var command in GetCommands())
        {
            if (command.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new Suggestion(command.Name, command.Description, IsCommand: true));
            }
        }

        var convars = ConVars;
        if (convars != null)
        {
            foreach (var convar in convars.GetAll())
            {
                if (convar.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(new Suggestion(convar.Name, convar.Description, IsCommand: false));
                }
            }
        }

        var ordered = items
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSuggestions)
            .ToList();

        if (ordered.Count == 1 && ordered[0].Name.Equals(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _suggestions.AddRange(ordered);
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
