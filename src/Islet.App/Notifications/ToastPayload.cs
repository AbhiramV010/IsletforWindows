using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Islet.App.Notifications;

/// <summary>Turns a stored toast (its XML payload and the app that sent it) into a <see cref="NotificationItem"/>.</summary>
public static partial class ToastPayload
{
    private const int MaxFieldChars = 300;

    /// <summary>
    /// A toast's text elements read title first, then the body lines. Elements marked as the
    /// attribution line (like "via Messages") are left out. Null if there is no text to show, or
    /// the payload isn't toast XML.
    /// </summary>
    public static NotificationItem? ToItem(string? primaryId, string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        List<string> texts = document
            .Descendants("text")
            .Where(element => !string.Equals((string?)element.Attribute("placement"), "attribution", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Value.Trim())
            .Where(text => text.Length > 0)
            .ToList();

        return texts.Count == 0
            ? null
            : new NotificationItem(
                AppNames.From(primaryId),
                Clip(texts[0]),
                Clip(string.Join(" ", texts.Skip(1))));
    }

    private static string Clip(string text) =>
        text.Length <= MaxFieldChars ? text : text[..(MaxFieldChars - 1)] + "…";
}

/// <summary>A readable app name from the id Windows stores for the sender. Best effort.</summary>
public static partial class AppNames
{
    public const string Fallback = "Notification";

    /// <summary>"Microsoft.WindowsAlarms_8wekyb3d8bbwe!App" becomes "Windows Alarms", and a path or "Spotify.exe" its program name.</summary>
    public static string From(string? primaryId)
    {
        if (string.IsNullOrWhiteSpace(primaryId))
        {
            return Fallback;
        }

        string name = primaryId.Trim();

        if (name.Contains('!'))
        {
            // Packaged app: "<publisher>.<Name>_<hash>!<entry>".
            name = name[..name.IndexOf('!')];
            int underscore = name.IndexOf('_');
            if (underscore > 0)
            {
                name = name[..underscore];
            }

            int dot = name.LastIndexOf('.');
            if (dot >= 0 && dot < name.Length - 1)
            {
                name = name[(dot + 1)..];
            }
        }
        else if (name.Contains('\\') || name.Contains('/'))
        {
            name = Path.GetFileNameWithoutExtension(name);
        }
        else
        {
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^4];
            }

            // Dotted ids name the program last ("com.squirrel.slack.slack", "Microsoft.Teams"), unless
            // that last part is an install token, as in "Brave._crx_abc".
            string[] parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                name = parts[^1].StartsWith('_') ? parts[0] : parts[^1];
            }
        }

        name = CamelBoundary().Replace(name, " ").Replace('_', ' ').Trim();
        if (name.Length == 0)
        {
            return Fallback;
        }

        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>The gap between a lowercase letter and a capital: "WindowsAlarms" → "Windows Alarms".</summary>
    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex CamelBoundary();
}
