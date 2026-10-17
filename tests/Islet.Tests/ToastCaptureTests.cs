using Islet.App.Notifications;

namespace Islet.Tests;

public class ToastPayloadTests
{
    [Fact]
    public void Title_and_body_come_from_the_text_elements()
    {
        NotificationItem item = ToastPayload.ToItem(
            "Microsoft.WindowsAlarms_8wekyb3d8bbwe!App",
            """<toast><visual><binding template="ToastGeneric"><text>Timer done</text><text>Your 5 minute timer has finished</text></binding></visual></toast>""")!;

        Assert.Equal(new NotificationItem("Windows Alarms", "Timer done", "Your 5 minute timer has finished"), item);
    }

    [Fact]
    public void Extra_lines_join_into_the_body()
    {
        NotificationItem item = ToastPayload.ToItem(
            "Chrome",
            """<toast><visual><binding template="ToastGeneric"><text>Subject</text><text>line one</text><text>line two</text></binding></visual></toast>""")!;

        Assert.Equal("line one line two", item.Body);
    }

    [Fact]
    public void A_title_only_toast_has_an_empty_body()
    {
        NotificationItem item = ToastPayload.ToItem(
            "Chrome", """<toast><visual><binding template="ToastGeneric"><text>Just a title</text></binding></visual></toast>""")!;
        Assert.Equal(new NotificationItem("Chrome", "Just a title", ""), item);
    }

    [Fact]
    public void The_attribution_line_is_left_out()
    {
        NotificationItem item = ToastPayload.ToItem(
            "Chrome",
            """<toast><visual><binding template="ToastGeneric"><text>Title</text><text>Body</text><text placement="attribution">via Messages</text></binding></visual></toast>""")!;

        Assert.Equal("Body", item.Body);
    }

    [Fact]
    public void Special_characters_survive()
    {
        NotificationItem item = ToastPayload.ToItem(
            "Chrome", """<toast><visual><binding><text>Fish &amp; chips</text><text>5 &lt; 6 — ok</text></binding></visual></toast>""")!;

        Assert.Equal("Fish & chips", item.Title);
        Assert.Equal("5 < 6 — ok", item.Body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not xml")]
    [InlineData("<toast><visual><binding></binding></visual></toast>")]
    [InlineData("<toast><visual><binding><text>   </text></binding></visual></toast>")]
    public void Nothing_to_show_is_null(string? xml) => Assert.Null(ToastPayload.ToItem("Chrome", xml));

    [Fact]
    public void Very_long_text_is_clipped()
    {
        NotificationItem item = ToastPayload.ToItem(
            "Chrome", $"<toast><visual><binding><text>T</text><text>{new string('x', 2000)}</text></binding></visual></toast>")!;

        Assert.Equal(300, item.Body.Length);
        Assert.EndsWith("…", item.Body);
    }
}

public class AppNamesTests
{
    [Theory]
    [InlineData("Microsoft.WindowsAlarms_8wekyb3d8bbwe!App", "Windows Alarms")]
    [InlineData("Microsoft.WindowsStore_8wekyb3d8bbwe!App", "Windows Store")]
    [InlineData("5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App", "Whats App Desktop")]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("Chrome", "Chrome")]
    [InlineData("Brave._crx_eppojlglocmnohnlnionkobfln", "Brave")]
    [InlineData("Microsoft.Teams", "Teams")]
    [InlineData(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe", "Powershell")]
    [InlineData("com.squirrel.slack.slack", "Slack")]
    public void Names_are_made_readable(string id, string expected) => Assert.Equal(expected, AppNames.From(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_ids_fall_back(string? id) => Assert.Equal("Notification", AppNames.From(id));
}

/// <summary>The reader, end to end against a database laid out like Windows' own.</summary>
public class NotificationWatcherTests : IDisposable
{
    private const string Schema =
        "CREATE TABLE NotificationHandler (RecordId INTEGER PRIMARY KEY, PrimaryId TEXT);" +
        "CREATE TABLE Notification (Id INTEGER PRIMARY KEY, HandlerId INTEGER, Type TEXT, Payload BLOB);";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"islet-wpn-test-{Guid.NewGuid():N}");

    public NotificationWatcherTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Temp folder; left for cleanup.
        }
    }

    [Fact]
    public void Existing_toasts_are_not_replayed_but_new_ones_arrive_once_and_in_order()
    {
        CreateDatabase(
            "INSERT INTO NotificationHandler VALUES (1, 'Chrome');" +
            Toast(1, "Old", "from before Islet started"));

        var seen = new List<NotificationItem>();
        using var watcher = new NotificationWatcher(seen.Add, _folder);
        StartAndWait(watcher);

        Assert.Empty(watcher.Read());

        Add(Toast(2, "First", "one") + Toast(3, "Second", "two"));
        IReadOnlyList<NotificationItem> items = watcher.Read();

        Assert.Equal(new[] { "First", "Second" }, items.Select(item => item.Title).ToArray());
        Assert.Equal("Chrome", items[0].App);
        Assert.Equal(new[] { "First", "Second" }, seen.Select(item => item.Title).ToArray());

        Assert.Empty(watcher.Read());
    }

    [Fact]
    public void Only_toasts_count_not_tiles_or_badges()
    {
        CreateDatabase("INSERT INTO NotificationHandler VALUES (1, 'Chrome');");
        using var watcher = new NotificationWatcher(_ => { }, _folder);
        StartAndWait(watcher);

        Add(
            "INSERT INTO Notification VALUES (2, 1, 'tile', '<tile><visual><binding><text>tile</text></binding></visual></tile>');" +
            "INSERT INTO Notification VALUES (3, 1, 'badge', '<badge value=\"3\"/>');" +
            Toast(4, "Real toast", "shown"));

        Assert.Equal("Real toast", Assert.Single(watcher.Read()).Title);
    }

    [Fact]
    public void A_toast_from_an_unknown_handler_still_shows_with_a_generic_app_name()
    {
        CreateDatabase(string.Empty);
        using var watcher = new NotificationWatcher(_ => { }, _folder);
        StartAndWait(watcher);

        Add(Toast(2, "Orphan", "no handler row", handler: 99));

        NotificationItem item = Assert.Single(watcher.Read());
        Assert.Equal("Notification", item.App);
    }

    [Fact]
    public void A_missing_database_is_logged_not_thrown()
    {
        using var watcher = new NotificationWatcher(_ => { }, _folder);
        watcher.Start();
        Thread.Sleep(300);
        Assert.Empty(watcher.Read());
    }

    [Fact]
    public async Task A_database_change_is_noticed_without_polling()
    {
        CreateDatabase("INSERT INTO NotificationHandler VALUES (1, 'Chrome');");
        var arrived = new TaskCompletionSource<NotificationItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new NotificationWatcher(item => arrived.TrySetResult(item), _folder);
        StartAndWait(watcher);

        Add(Toast(2, "Pushed", "by a file change"));

        NotificationItem item = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Pushed", item.Title);
    }

    private void StartAndWait(NotificationWatcher watcher)
    {
        watcher.Start();
        // Start runs on the pool; give it time to take the high-water mark and arm the file watcher.
        Thread.Sleep(600);
    }

    private static string Toast(long id, string title, string body, long handler = 1) =>
        $"INSERT INTO Notification VALUES ({id}, {handler}, 'toast', " +
        $"'<toast><visual><binding template=\"ToastGeneric\"><text>{title}</text><text>{body}</text></binding></visual></toast>');";

    private void CreateDatabase(string statements)
    {
        using SqliteDatabase database = SqliteDatabase.Create(Path.Combine(_folder, NotificationWatcher.DatabaseName));
        database.Execute(Schema);
        if (statements.Length > 0)
        {
            database.Execute(statements);
        }
    }

    private void Add(string statements)
    {
        using SqliteDatabase database = SqliteDatabase.Open(Path.Combine(_folder, NotificationWatcher.DatabaseName));
        database.Execute(statements);
    }
}
