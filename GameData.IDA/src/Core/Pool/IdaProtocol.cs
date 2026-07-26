using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameData.IDA.Core.Pool;

internal static class IdaProtocol
{
    internal static string Write(WorkerMessage message)
        => JsonSerializer.Serialize(message, IdaProtocolContext.Default.WorkerMessage);

    internal static string Write(PoolMessage message)
        => JsonSerializer.Serialize(message, IdaProtocolContext.Default.PoolMessage);

    internal static WorkerMessage? ReadWorker(string line)
    {
        if (!TryTrim(line, out string json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, IdaProtocolContext.Default.WorkerMessage);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static PoolMessage? ReadPool(string line)
    {
        if (!TryTrim(line, out string json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, IdaProtocolContext.Default.PoolMessage);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryTrim(string line, out string json)
    {
        json = line.Trim().TrimStart('﻿');
        return json.Length > 0 && json[0] == '{';
    }
}

internal sealed record PoolMessage(
    [property: JsonPropertyName("t")] string T,
    [property: JsonPropertyName("id")] int Id = 0,
    [property: JsonPropertyName("path")] string? Path = null,
    [property: JsonPropertyName("save")] bool Save = true);

internal sealed record WorkerMessage(
    [property: JsonPropertyName("t")] string T,
    [property: JsonPropertyName("id")] int Id = 0,
    [property: JsonPropertyName("sdk")] string? Sdk = null,
    [property: JsonPropertyName("version")] string? Version = null,
    [property: JsonPropertyName("frac")] double Fraction = 0.0,
    [property: JsonPropertyName("ea")] string? Address = null,
    [property: JsonPropertyName("funcs")] int Functions = 0,
    [property: JsonPropertyName("segs")] int Segments = 0,
    [property: JsonPropertyName("ms")] long Milliseconds = 0,
    [property: JsonPropertyName("message")] string? Message = null);

internal static class IdaProtocolKind
{
    internal const string Job = "job";
    internal const string Quit = "quit";

    internal const string Ready = "ready";
    internal const string Progress = "progress";
    internal const string Done = "done";
    internal const string Failed = "failed";
}

[JsonSerializable(typeof(PoolMessage))]
[JsonSerializable(typeof(WorkerMessage))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class IdaProtocolContext : JsonSerializerContext;
