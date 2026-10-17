using System.Runtime.InteropServices;

namespace Islet.Hook;

/// <summary>Cheap "is the app running" check, so hooks don't spend the whole connect timeout when it isn't.</summary>
internal static partial class PipeProbe
{
    private const int ErrorFileNotFound = 2;

    /// <summary>
    /// False only when no pipe with this name exists. A busy or slow pipe still counts as
    /// present, and the normal connect (with its timeout) decides.
    /// </summary>
    public static bool Exists(string pipeName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        // WaitNamedPipe fails at once with ERROR_FILE_NOT_FOUND when nothing has created the pipe.
        if (WaitNamedPipe($@"\\.\pipe\{pipeName}", 1))
        {
            return true;
        }

        return Marshal.GetLastPInvokeError() != ErrorFileNotFound;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WaitNamedPipe(string name, uint timeoutMs);
}
