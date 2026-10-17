namespace Islet.App.Notifications;

/// <summary>Turns the text elements of a toast banner into a <see cref="NotificationItem"/>.</summary>
public static class ToastText
{
    private const int MaxFieldChars = 300;

    /// <summary>
    /// A banner lists its text as the app name, then the title, then the body lines. Fewer
    /// elements mean the app name was left out. Null if there is nothing worth showing.
    /// </summary>
    public static NotificationItem? Parse(IReadOnlyList<string> texts)
    {
        List<string> cleaned = texts
            .Select(text => text.Trim())
            .Where(text => text.Length > 0)
            .ToList();

        // Collapse back-to-back repeats: an element can expose its text twice (label and child).
        for (int i = cleaned.Count - 1; i > 0; i--)
        {
            if (cleaned[i] == cleaned[i - 1])
            {
                cleaned.RemoveAt(i);
            }
        }

        return cleaned.Count switch
        {
            0 => null,
            1 => Make("Notification", cleaned[0], ""),
            2 => Make("Notification", cleaned[0], cleaned[1]),
            _ => Make(cleaned[0], cleaned[1], string.Join(" ", cleaned.Skip(2))),
        };
    }

    private static NotificationItem Make(string app, string title, string body) =>
        new(Clip(app), Clip(title), Clip(body));

    private static string Clip(string text) =>
        text.Length <= MaxFieldChars ? text : text[..(MaxFieldChars - 1)] + "…";
}
