using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Islet.Hook;

/// <summary>Builds the hooks block for the user to paste. The shim never edits agent settings itself.</summary>
internal static class HookConfig
{
    private static readonly string[] ClaudeFireAndForgetEvents = ["Notification", "PostToolUse", "Stop"];

    /// <summary>Codex has no Notification hook.</summary>
    private static readonly string[] CodexFireAndForgetEvents = ["PostToolUse", "Stop"];

    /// <summary>These return at once, so a short timeout stops a stuck shim holding up Codex.</summary>
    private const int CodexQuickTimeoutSeconds = 10;

    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Indented = true,

        // Keep quotes readable: this text gets pasted into a settings file, not HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Gives the user time to walk back to the desk before the agent gives up.</summary>
    private const int PermissionTimeoutSeconds = 600;

    public static string? Build(string agent, string exePath) => agent.ToLowerInvariant() switch
    {
        "claude" => BuildClaude(exePath),
        "codex" => BuildCodex(exePath),
        _ => null,
    };

    /// <summary>
    /// Claude Code runs hook commands through Git Bash on Windows (PowerShell without it).
    /// A quoted forward-slash path works in both.
    /// </summary>
    public static string CommandFor(string exePath, string agent) => $"\"{exePath.Replace('\\', '/')}\" {agent}";

    /// <summary>
    /// Codex's hooks reference doesn't say which shell runs hook commands on Windows, and cmd.exe
    /// and PowerShell disagree about a quoted program path (PowerShell needs a leading call
    /// operator). A path with no spaces needs no quotes, so it is written bare, which both accept.
    /// </summary>
    public static string CommandWindowsFor(string exePath, string agent)
    {
        string path = exePath.Replace('/', '\\');
        return path.Any(char.IsWhiteSpace) ? $"\"{path}\" {agent}" : $"{path} {agent}";
    }

    private static string BuildCodex(string exePath)
    {
        string command = CommandFor(exePath, "codex");
        string commandWindows = CommandWindowsFor(exePath, "codex");

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, JsonOptions))
        {
            json.WriteStartObject();
            json.WriteStartObject("hooks");

            WriteCodexEvent(json, "PermissionRequest", command, commandWindows, PermissionTimeoutSeconds);
            foreach (string eventName in CodexFireAndForgetEvents)
            {
                WriteCodexEvent(json, eventName, command, commandWindows, CodexQuickTimeoutSeconds);
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + Environment.NewLine;
    }

    private static void WriteCodexEvent(
        Utf8JsonWriter json, string eventName, string command, string commandWindows, int timeoutSeconds)
    {
        json.WriteStartArray(eventName);
        json.WriteStartObject();
        json.WriteStartArray("hooks");
        json.WriteStartObject();
        json.WriteString("type", "command");
        json.WriteString("command", command);
        json.WriteString("commandWindows", commandWindows);
        json.WriteNumber("timeout", timeoutSeconds);
        json.WriteEndObject();
        json.WriteEndArray();
        json.WriteEndObject();
        json.WriteEndArray();
    }

    private static string BuildClaude(string exePath)
    {
        string command = CommandFor(exePath, "claude");

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, JsonOptions))
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
