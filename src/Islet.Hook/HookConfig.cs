using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Islet.Hook;

/// <summary>Builds the hooks block for the user to paste. The shim never edits agent settings itself.</summary>
internal static class HookConfig
{
    private static readonly string[] ClaudeFireAndForgetEvents = ["Notification", "PostToolUse", "Stop"];

    /// <summary>Gives the user time to walk back to the desk before Claude Code gives up.</summary>
    private const int PermissionTimeoutSeconds = 600;

    public static string? Build(string agent, string exePath) => agent.ToLowerInvariant() switch
    {
        "claude" => BuildClaude(exePath),
        _ => null,
    };

    /// <summary>
    /// Claude Code runs hook commands through Git Bash on Windows (PowerShell without it).
    /// A quoted forward-slash path works in both.
    /// </summary>
    public static string CommandFor(string exePath, string agent) => $"\"{exePath.Replace('\\', '/')}\" {agent}";

    private static string BuildClaude(string exePath)
    {
        string command = CommandFor(exePath, "claude");

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = true,

            // Write quotes as \" rather than ": this text gets pasted into a settings file, not HTML.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            json.WriteStartObject();
            json.WriteStartObject("hooks");

            WriteEvent(json, "PermissionRequest", command, PermissionTimeoutSeconds);
            foreach (string eventName in ClaudeFireAndForgetEvents)
            {
                WriteEvent(json, eventName, command, timeoutSeconds: null);
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + Environment.NewLine;
    }

    private static void WriteEvent(Utf8JsonWriter json, string eventName, string command, int? timeoutSeconds)
    {
        json.WriteStartArray(eventName);
        json.WriteStartObject();
        json.WriteStartArray("hooks");
        json.WriteStartObject();
        json.WriteString("type", "command");
        json.WriteString("command", command);
        if (timeoutSeconds is int timeout)
        {
            json.WriteNumber("timeout", timeout);
        }

        json.WriteEndObject();
        json.WriteEndArray();
        json.WriteEndObject();
        json.WriteEndArray();
    }
}
