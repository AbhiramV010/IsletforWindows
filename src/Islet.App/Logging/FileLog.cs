using System.IO;

namespace Islet.App;

/// <summary>
/// Plain append-only log at %LOCALAPPDATA%\Islet\logs\app.log.
/// Logging must never take the app down, so every failure is swallowed.
/// </summary>
internal static class FileLog
{
    private static readonly object Gate = new();

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        IsletInfo.Name,
        "logs");

    public static string LogFilePath => Path.Combine(LogDirectory, "app.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogFilePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Never let logging fail the app.
        }
    }

    public static void Write(string context, Exception exception) =>
        Write($"{context}: {exception.GetType().Name}: {exception.Message}");
}
