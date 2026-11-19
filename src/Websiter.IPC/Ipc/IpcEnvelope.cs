using System.Text.Json;
using System.Text.Json.Serialization;

namespace Websiter.Ipc;

/// <summary>Kind of an IPC envelope.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IpcMessageKind>))]
public enum IpcMessageKind
{
    /// <summary>Request expecting a response (request-response).</summary>
    Request,
    /// <summary>Response correlated to a request.</summary>
    Response,
    /// <summary>One-way fire-and-forget message.</summary>
    Send,
    /// <summary>Published event delivered to subscribers.</summary>
    Event,
}

/// <summary>Current IPC protocol version. Bumped on breaking envelope changes.</summary>
public static class IpcProtocol
{
    public const int Version = 1;
}

/// <summary>
/// Wire envelope for all Websiter IPC traffic. Serialized as length-prefixed
/// UTF-8 JSON. Delivery semantics: at-least-once for events, exactly-once
/// request/response correlation within a connection (no exactly-once guarantee
/// is claimed for sends across reconnects).
/// </summary>
public sealed class IpcEnvelope
{
    /// <summary>Protocol version.</summary>
    [JsonPropertyName("v")] public int Version { get; set; } = IpcProtocol.Version;

    /// <summary>Unique message id.</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Correlation id linking a response to its request.</summary>
    [JsonPropertyName("corr")] public string? CorrelationId { get; set; }

    /// <summary>Message kind.</summary>
    [JsonPropertyName("kind")] public IpcMessageKind Kind { get; set; }

    /// <summary>Sender identity (worker name, "host", or process id fallback).</summary>
    [JsonPropertyName("from")] public string From { get; set; } = "unknown";

    /// <summary>Target worker name, or topic expression for events.</summary>
    [JsonPropertyName("to")] public string To { get; set; } = string.Empty;

    /// <summary>Handler action / message type name.</summary>
    [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;

    /// <summary>Whether a response envelope indicates success.</summary>
    [JsonPropertyName("ok")] public bool Ok { get; set; } = true;

    /// <summary>Error details when <see cref="Ok"/> is false.</summary>
    [JsonPropertyName("err")] public string? Error { get; set; }

    /// <summary>Message payload as raw JSON.</summary>
    [JsonPropertyName("payload")] public JsonElement? Payload { get; set; }

    /// <summary>UTC timestamp.</summary>
    [JsonPropertyName("ts")] public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    private static readonly JsonSerializerOptions _options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
    };

    public byte[] ToBytes()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(this, _options);
        return json;
    }

    public static IpcEnvelope FromBytes(ReadOnlySpan<byte> utf8Json)
    {
        var envelope = JsonSerializer.Deserialize<IpcEnvelope>(utf8Json, _options)
            ?? throw new IpcException("Failed to deserialize IPC envelope.");
        if (envelope.Version != IpcProtocol.Version)
            throw new IpcException($"Unsupported IPC protocol version {envelope.Version}; expected {IpcProtocol.Version}.");
        return envelope;
    }

    /// <summary>Creates an envelope carrying an arbitrary payload object.</summary>
    public static IpcEnvelope Create(IpcMessageKind kind, string to, string action, object? payload = null)
    {
        JsonElement? element = payload switch
        {
            null => null,
            JsonElement e => e,
            _ => JsonSerializer.SerializeToElement(payload),
        };
        return new IpcEnvelope
        {
            Kind = kind,
            To = to,
            Action = action,
            Payload = element,
        };
    }

    /// <summary>Deserializes the payload to <typeparamref name="T"/>.</summary>
    public T? GetPayload<T>()
    {
        if (Payload is null) return default;
        return Payload.Value.Deserialize<T>();
    }
}
