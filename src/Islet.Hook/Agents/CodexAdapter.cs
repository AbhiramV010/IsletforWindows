using System.Text.Json;
using System.Text.Json.Serialization;
using Islet.Hook.Protocol;

namespace Islet.Hook.Agents;

/// <summary>
/// Codex hooks, per the Codex hooks reference for codex-cli 0.163. Input is close to Claude
/// Code's but not the same: it adds <c>turn_id</c> and <c>model</c>, has no <c>Notification</c>
/// event, and uses <c>apply_patch</c> and MCP tool names. For a <c>PermissionRequest</c>,
/// <c>tool_input.command</c> holds the command or patch, and <c>tool_input.description</c> the
/// reason for asking.
///
/// Output: allow and deny use the same <c>hookSpecificOutput</c> shape as Claude Code, but Codex
/// fails closed on <c>updatedInput</c>, <c>updatedPermissions</c> and <c>interrupt</c>, so this
/// serializer has no way to emit them. A deny must carry a message.
/// </summary>
internal sealed class CodexAdapter : IAgentAdapter
{
    public const string DefaultDenyMessage = "Denied from Islet";

    public string Name => "codex";

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
        };
    }

    public string? FormatDecision(HookReply reply)
    {
        CodexDecision? decision = reply.Decision switch
        {
            Decisions.Allow => new CodexDecision { Behavior = "allow" },
            Decisions.Deny => new CodexDecision
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

        var output = new CodexHookOutput
        {
            HookSpecificOutput = new CodexHookSpecificOutput
            {
                HookEventName = PipeProtocol.PermissionRequestEvent,
                Decision = decision,
            },
        };
        return JsonSerializer.Serialize(output, CodexJsonContext.Default.CodexHookOutput);
    }

    private static string? GetString(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

internal sealed class CodexHookOutput
{
    [JsonPropertyName("hookSpecificOutput")]
    public CodexHookSpecificOutput? HookSpecificOutput { get; set; }
}

internal sealed class CodexHookSpecificOutput
{
    [JsonPropertyName("hookEventName")]
    public string HookEventName { get; set; } = "";

    [JsonPropertyName("decision")]
    public CodexDecision? Decision { get; set; }
}

internal sealed class CodexDecision
{
    /// <summary><c>allow</c> or <c>deny</c>.</summary>
    [JsonPropertyName("behavior")]
    public string Behavior { get; set; } = "";

    /// <summary>Deny only.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CodexHookOutput))]
internal sealed partial class CodexJsonContext : JsonSerializerContext;
