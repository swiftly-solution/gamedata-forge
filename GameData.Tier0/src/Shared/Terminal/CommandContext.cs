namespace GameData.Tier0.Shared.Terminal;

public sealed class CommandContext
{
    public required string Name { get; init; }
    public required string[] Args { get; init; }
    public required string ArgString { get; init; }
    public required ITerminal Terminal { get; init; }
    public required Action<string> Print { get; init; }
    public required Action<string> Warn { get; init; }
}
