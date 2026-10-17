using System.Runtime.InteropServices;

namespace Islet.App.Notifications;

/// <summary>
/// A tiny read/query wrapper over <c>winsqlite3.dll</c>, the SQLite that ships with Windows
/// (10 1809 and later). Using the system copy means no NuGet package. Only what the
/// notification reader needs: open, run a statement, and read rows of integers and text.
/// </summary>
internal sealed class SqliteDatabase : IDisposable
{
    private const int OpenReadWrite = 0x2;
    private const int OpenCreate = 0x4;
    private const int Row = 100;
    private const int Done = 101;
    private const int NullType = 5;

    private IntPtr _db;

    private SqliteDatabase(IntPtr db) => _db = db;

    /// <summary>Opens an existing database for reading and writing. SQLite needs write access to rebuild a WAL index, so only ever open a private copy this way.</summary>
    public static SqliteDatabase Open(string path) => Open(path, OpenReadWrite);

    /// <summary>Creates the database if it doesn't exist. For tests.</summary>
    public static SqliteDatabase Create(string path) => Open(path, OpenReadWrite | OpenCreate);

    public void Dispose()
    {
        if (_db != IntPtr.Zero)
        {
            sqlite3_close(_db);
            _db = IntPtr.Zero;
        }
    }

    public void Execute(string sql)
    {
        if (sqlite3_exec(_db, sql, IntPtr.Zero, IntPtr.Zero, out IntPtr error) != 0)
        {
            string message = error != IntPtr.Zero ? Marshal.PtrToStringUTF8(error) ?? "" : LastError();
            throw new InvalidOperationException($"SQLite: {message}");
        }
    }

    /// <summary>Runs a query, with at most one integer parameter bound to <c>?1</c>, and maps each row.</summary>
    public IReadOnlyList<T> Query<T>(string sql, Func<SqliteRow, T> map, long? parameter = null)
    {
        if (sqlite3_prepare_v2(_db, sql, -1, out IntPtr statement, IntPtr.Zero) != 0)
        {
            throw new InvalidOperationException($"SQLite: {LastError()}");
        }

        try
        {
            if (parameter is { } value)
            {
                sqlite3_bind_int64(statement, 1, value);
            }

            var rows = new List<T>();
            int result;
            while ((result = sqlite3_step(statement)) == Row)
            {
                rows.Add(map(new SqliteRow(statement)));
            }

            if (result != Done)
            {
                throw new InvalidOperationException($"SQLite: {LastError()}");
            }

            return rows;
        }
        finally
        {
            sqlite3_finalize(statement);
        }
    }

    private static SqliteDatabase Open(string path, int flags)
    {
        int result = sqlite3_open_v2(path, out IntPtr db, flags, IntPtr.Zero);
        if (result != 0)
        {
            string message = db != IntPtr.Zero ? Marshal.PtrToStringUTF8(sqlite3_errmsg(db)) ?? "" : $"code {result}";
            if (db != IntPtr.Zero)
            {
                sqlite3_close(db);
            }

            throw new InvalidOperationException($"SQLite could not open the database: {message}");
        }

        return new SqliteDatabase(db);
    }

    private string LastError() => Marshal.PtrToStringUTF8(sqlite3_errmsg(_db)) ?? "unknown error";

    /// <summary>One row of a query result, valid only inside the mapping callback.</summary>
    internal readonly struct SqliteRow(IntPtr statement)
    {
        public long Int64(int column) => sqlite3_column_int64(statement, column);

        /// <summary>Text, or null for SQL NULL.</summary>
        public string? Text(int column) =>
            sqlite3_column_type(statement, column) == NullType
                ? null
                : Marshal.PtrToStringUTF8(sqlite3_column_text(statement, column));
    }

    private const string Library = "winsqlite3.dll";

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr db);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(
        IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr argument, out IntPtr error);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(
        IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_column_int64(IntPtr statement, int column);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_type(IntPtr statement, int column);
}
