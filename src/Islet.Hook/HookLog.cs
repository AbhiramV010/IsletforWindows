using System.Text;

namespace Islet.Hook;

/// <summary>
/// Append-only log at %LOCALAPPDATA%\Islet\logs\hook.log. Stdout is reserved for decision
/// JSON, so the shim never logs anywhere else. Every failure is swallowed.
/// </summary>
internal static class HookLog
{
    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(ProductInfo.LogDirectory);

            // Several hooks can fire at once, so share the file and write each line in one call.
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}";
            using var stream = new FileStream(
                Path.Combine(ProductInfo.LogDirectory, "hook.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            stream.Write(Encoding.UTF8.GetBytes(line));
        }
        catch
        {
            // Logging must never change the shim's outcome.
        }
    }
}
