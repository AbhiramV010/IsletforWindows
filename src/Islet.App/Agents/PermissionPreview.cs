using System.IO;
using System.Text;
using System.Text.Json;

namespace Islet.App.Agents;

/// <summary>What the permission card shows for a tool call: one line, plus the full text for the tooltip.</summary>
public readonly record struct PermissionPreview(string Line, string Full)
{
    private const int MaxLineChars = 300;
    private const int MaxFullChars = 4000;

    public static PermissionPreview From(string? tool, JsonElement? input)
    {
        string? primary = input is { ValueKind: JsonValueKind.Object } value ? PrimaryField(tool, value) : null;
        string json = input is { } raw ? Indented(raw) : "";

        string line = OneLine(primary ?? (input is { } compact ? compact.GetRawText() : ""));
        string full = primary is null || IsShell(tool) ? primary ?? json : $"{primary}{Environment.NewLine}{Environment.NewLine}{json}";

        return new PermissionPreview(Truncate(line, MaxLineChars), Truncate(full, MaxFullChars));
    }

    /// <summary>The question texts from an <c>AskUserQuestion</c> input, one per paragraph. Null if there are none.</summary>
    public static string? QuestionText(JsonElement? input)
    {
        if (input is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("questions", out JsonElement questions) ||
            questions.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var lines = new List<string>();
        foreach (JsonElement item in questions.EnumerateArray())
        {
            if (GetString(item, "question") is { Length: > 0 } text)
            {
                lines.Add(text);
            }
        }

        return lines.Count == 0
            ? null
            : Truncate(string.Join(Environment.NewLine + Environment.NewLine, lines), MaxFullChars);
    }

    /// <summary>Last segment of the working directory: the repo or folder name.</summary>
    public static string RepoName(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return "";
        }

        string trimmed = cwd.TrimEnd('/', '\\');
        int slash = trimmed.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    private static bool IsShell(string? tool) => tool is "Bash" or "PowerShell";

    /// <summary>The one field that says what the call does, per tool.</summary>
    private static string? PrimaryField(string? tool, JsonElement input) => tool switch
    {
        "Bash" or "PowerShell" => GetString(input, "command"),
        "Edit" or "MultiEdit" or "Write" or "Read" => GetString(input, "file_path"),
        "NotebookEdit" => GetString(input, "notebook_path"),
        "WebFetch" => GetString(input, "url"),
        "WebSearch" => GetString(input, "query"),
        "Glob" or "Grep" => GetString(input, "pattern"),
        _ => null,
    };

    private static string? GetString(JsonElement input, string property) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string OneLine(string text)
    {
        var builder = new StringBuilder(Math.Min(text.Length, MaxLineChars + 1));
        foreach (string part in text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            if (builder.Length > 0)
            {
                builder.Append(" ⏎ ");
            }

            builder.Append(part.Trim());
            if (builder.Length > MaxLineChars)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static string Indented(JsonElement element)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            element.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
