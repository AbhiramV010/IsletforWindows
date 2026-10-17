using System.Text.Json;
using System.Text.Json.Serialization;
using Islet.Hook.Protocol;

namespace Islet.Hook.Agents;

/// <summary>
/// Claude Code hooks, per https://code.claude.com/docs/en/hooks. Common input fields are
/// <c>session_id</c>, <c>cwd</c> and <c>hook_event_name</c>. Tool events add <c>tool_name</c>
/// and <c>tool_input</c>, and <c>Notification</c> adds <c>message</c>.
/// </summary>
internal sealed class ClaudeAdapter : IAgentAdapter
{
    public const string DefaultDenyMessage = "Denied from Islet";

    public string Name => "claude";

    public HookMessage? Map(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? eventName = GetString(payload, "hook_event_name");
        if (string.IsNullOrEmpty(eventName))
        {
            return null;
        }

        return new HookMessage
        {
            Agent = Name,
            Event = eventName,
            SessionId = GetString(payload, "session_id"),
            Cwd = GetString(payload, "cwd"),
            Tool = GetString(payload, "tool_name"),
            Input = payload.TryGetProperty("tool_input", out JsonElement input) ? input.Clone() : null,
            Message = GetString(payload, "message"),
            NotificationType = GetString(payload, "notification_type"),
        };
    }

    public string? FormatDecision(HookReply reply)
    {
        ClaudeDecision? decision = reply.Decision switch
        {
            Decisions.Allow => new ClaudeDecision { Behavior = "allow" },
            Decisions.Deny => new ClaudeDecision
            {
                Behavior = "deny",
                Message = string.IsNullOrWhiteSpace(reply.Message) ? DefaultDenyMessage : reply.Message,
            },
            _ => null,
        };

        if (decision is null)
        {
            return null;
        }

        var output = new ClaudeHookOutput
        {
            HookSpecificOutput = new ClaudeHookSpecificOutput
            {
                HookEventName = PipeProtocol.PermissionRequestEvent,
                Decision = decision,
            },
        };
        return JsonSerializer.Serialize(output, ClaudeJsonContext.Default.ClaudeHookOutput);
    }

    private static string? GetString(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

internal sealed class ClaudeHookOutput
{
    [JsonPropertyName("hookSpecificOutput")]
    public ClaudeHookSpecificOutput? HookSpecificOutput { get; set; }
}

internal sealed class ClaudeHookSpecificOutput
{
    [JsonPropertyName("hookEventName")]
    public string HookEventName { get; set; } = "";

    [JsonPropertyName("decision")]
    public ClaudeDecision? Decision { get; set; }
}

internal sealed class ClaudeDecision
{
    /// <summary><c>allow</c> or <c>deny</c>.</summary>
    [JsonPropertyName("behavior")]
    public string Behavior { get; set; } = "";

    /// <summary>Deny only: tells Claude why.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ClaudeHookOutput))]
internal sealed partial class ClaudeJsonContext : JsonSerializerContext;
