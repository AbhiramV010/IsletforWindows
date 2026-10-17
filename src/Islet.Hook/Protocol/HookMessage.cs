using System.Text.Json;
using System.Text.Json.Serialization;

namespace Islet.Hook.Protocol;

/// <summary>One shim-to-app line. Agent adapters fill it from the agent's raw hook payload.</summary>
public sealed class HookMessage
{
    [JsonPropertyName("v")]
    public int Version { get; set; } = PipeProtocol.Version;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary><c>claude</c> or <c>codex</c>.</summary>
    [JsonPropertyName("agent")]
    public string Agent { get; set; } = "";

    /// <summary>The agent's hook event name, e.g. <c>PermissionRequest</c>.</summary>
    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("tool")]
    public string? Tool { get; set; }

    /// <summary>The tool's raw input, passed through untouched. The app decides what to show.</summary>
    [JsonPropertyName("input")]
    public JsonElement? Input { get; set; }

    /// <summary>Free text from the agent, e.g. a <c>Notification</c> message.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>Notification subtype, e.g. <c>permission_prompt</c> or <c>idle_prompt</c>. Optional, so v1 readers can ignore it.</summary>
    [JsonPropertyName("notificationType")]
    public string? NotificationType { get; set; }

    [JsonIgnore]
    public bool IsPermissionRequest => Event == PipeProtocol.PermissionRequestEvent;
}

/// <summary>App-to-shim line, sent only for permission requests.</summary>
public sealed class HookReply
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>One of <see cref="Decisions"/>.</summary>
    [JsonPropertyName("decision")]
    public string Decision { get; set; } = "";

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public static class Decisions
{
    public const string Allow = "allow";
    public const string Deny = "deny";
    public const string Passthrough = "passthrough";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HookMessage))]
[JsonSerializable(typeof(HookReply))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;
