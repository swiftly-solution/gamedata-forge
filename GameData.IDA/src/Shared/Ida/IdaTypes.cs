namespace GameData.IDA.Shared.Ida;

public sealed record IdaFunction(ulong Start, ulong End, string Name)
{
    public ulong Size => End - Start;
}

public sealed record IdaSegment(ulong Start, ulong End, string Name, string Class)
{
    public ulong Size => End - Start;
}

public readonly record struct IdaVersion(int Major, int Minor, int Build)
{
    public override string ToString() => $"{Major}.{Minor}.{Build}";
}

public readonly record struct IdaAnalysisProgress(ulong Address, ulong Start, ulong End, double Fraction);
