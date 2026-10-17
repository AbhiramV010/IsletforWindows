using System.IO;

namespace Islet.App.Notifications;

/// <summary>
/// Mirrors Windows toasts by reading the notification database Windows keeps for them
/// (<c>wpndatabase.db</c> in the user's Notifications folder), which is written the moment a toast
/// arrives. The official listener API needs an MSIX package identity, and toast banners can't be
/// read through UI Automation on current Windows, so this is the way that works without one.
///
/// It only ever reads a private snapshot copy, never the live database, and only looks at toasts
/// that arrive after Islet starts (no replaying history). It sees every toast Windows records,
/// including ones Focus Assist keeps off the screen. Text only; it can't click through to the
/// sending app. What is logged is the sending app's name, never the notification text.
/// </summary>
public sealed class NotificationWatcher : IDisposable
{
    public const string DatabaseName = "wpndatabase.db";

    private const string NewToasts =
        "SELECT n.Id, h.PrimaryId, CAST(n.Payload AS TEXT) " +
        "FROM Notification n LEFT JOIN NotificationHandler h ON h.RecordId = n.HandlerId " +
        "WHERE n.Id > ?1 AND n.Type = 'toast' ORDER BY n.Id";

    private const string HighestId = "SELECT COALESCE(MAX(Id), 0) FROM Notification";

    /// <summary>A toast is written in bursts of file changes; wait for them to settle.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);

    private readonly string _folder;
    private readonly Action<NotificationItem> _onToast;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private long _lastId;
    private bool _ready;
    private bool _disposed;

    public NotificationWatcher(Action<NotificationItem> onToast, string? folder = null)
    {
        _onToast = onToast;
        _folder = folder ?? DefaultFolder;
    }

    /// <summary>%LOCALAPPDATA%\Microsoft\Windows\Notifications.</summary>
    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Notifications");

    public void Start() => Task.Run(() =>
    {
        try
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                // Start from now: remember the newest toast already there and show only later ones.
                _lastId = ReadHighestId();
                _ready = true;
            }

            _debounce = new Timer(_ => Read(), null, Timeout.Infinite, Timeout.Infinite);
            _watcher = new FileSystemWatcher(_folder, DatabaseName + "*")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => Schedule();
            _watcher.Created += (_, _) => Schedule();
            FileLog.Write($"Toast capture on (notification database, newest id {_lastId})");
        }
        catch (Exception exception)
        {
            FileLog.Write("Toast capture unavailable", exception);
        }
    });

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _watcher?.Dispose();
        _debounce?.Dispose();
    }

    /// <summary>Reads the toasts that arrived since the last read. Also called directly by tests.</summary>
    internal IReadOnlyList<NotificationItem> Read()
    {
        var items = new List<NotificationItem>();
        lock (_gate)
        {
            if (_disposed || !_ready)
            {
                return items;
            }

            try
            {
                using var snapshot = new Snapshot(_folder);
                using SqliteDatabase database = snapshot.Open();

                IReadOnlyList<(long Id, string? App, string? Xml)> rows =
                    database.Query(NewToasts, row => (row.Int64(0), row.Text(1), row.Text(2)), _lastId);
                foreach ((long id, string? app, string? xml) in rows)
                {
                    _lastId = Math.Max(_lastId, id);
                    if (ToastPayload.ToItem(app, xml) is { } item)
                    {
                        items.Add(item);
                    }
                }
            }
            catch (Exception exception)
            {
                // A copy taken mid-write can be torn. The next change retries.
                FileLog.Write("Toast read failed", exception);
            }
        }

        foreach (NotificationItem item in items)
        {
            FileLog.Write($"Toast captured from '{item.App}'");
            _onToast(item);
        }

        return items;
    }

    private long ReadHighestId()
    {
        using var snapshot = new Snapshot(_folder);
        using SqliteDatabase database = snapshot.Open();
        return database.Query(HighestId, row => row.Int64(0)).FirstOrDefault();
    }

    private void Schedule()
    {
        try
        {
            _debounce?.Change(Settle, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Shut down while a change was pending.
        }
    }

    /// <summary>
    /// A private copy of the database and its write-ahead log in a temp folder. Windows has the
    /// real one open; a copy means Islet never holds a handle on it or writes beside it.
    /// </summary>
    private sealed class Snapshot : IDisposable
    {
        private readonly string _directory;
        private readonly string _database;

        public Snapshot(string sourceFolder)
        {
            _directory = Path.Combine(Path.GetTempPath(), $"islet-wpn-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
            _database = Path.Combine(_directory, DatabaseName);

            CopyShared(Path.Combine(sourceFolder, DatabaseName), _database);

            // The write-ahead log holds the newest rows. The shared-memory index is not copied:
            // SQLite rebuilds it from the log.
            string wal = Path.Combine(sourceFolder, DatabaseName + "-wal");
            if (File.Exists(wal))
            {
                CopyShared(wal, _database + "-wal");
            }
        }

        public SqliteDatabase Open() => SqliteDatabase.Open(_database);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // Left for the temp cleaner.
            }
        }

        private static void CopyShared(string from, string to)
        {
            using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = new FileStream(to, FileMode.Create, FileAccess.Write);
            source.CopyTo(target);
        }
    }
}
