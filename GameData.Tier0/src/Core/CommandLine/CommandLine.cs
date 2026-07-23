using GameData.Tier0.Shared.CommandLine;

namespace GameData.Tier0.Core.CommandLine;

internal class CCommandLine : ICommandLine
{
    private string _commandLine = "";
    private List<string> _parameters = [];
    private Dictionary<string, string> _parameterValues = [];

    public ulong ParameterCount => (ulong)_parameters.Count;

    public List<string> Parameters => _parameters;

    public string CommandLineString => _commandLine;

    public string GetParameterValue(string parameter, string defaultValue = "")
    {
        if (!_parameterValues.TryGetValue(parameter, out var value))
        {
            return defaultValue;
        }
        return value;
    }

    public bool HasParameter(string parameter)
    {
        return _parameterValues.ContainsKey(parameter);
    }

    public void Initialize(string commandLine)
    {
        _commandLine = commandLine;
        _parameters = [.. commandLine.Split(' ')];
        _parameterValues = [];
        for (int i = 0; i < _parameters.Count; i++)
        {
            if (_parameters[i].StartsWith('-'))
            {
                var key = _parameters[i][1..];
                if (i + 1 < _parameters.Count && !_parameters[i + 1].StartsWith('-'))
                {
                    _parameterValues[key] = _parameters[i + 1];
                    i++;
                }
                else
                {
                    _parameterValues[key] = "";
                }
            }
        }
    }

    public void Initialize(string[] args)
    {
        Initialize(string.Join(' ', args));
    }

    public void InitializeWithAppName(string commandLine)
    {
        var args = commandLine.Split(' ');
        if (args.Length > 0)
        {
            var appName = args[0];
            var remainingArgs = args[1..];
            Initialize(string.Join(' ', remainingArgs));
        }
        else
        {
            Initialize(commandLine);
        }
    }
}