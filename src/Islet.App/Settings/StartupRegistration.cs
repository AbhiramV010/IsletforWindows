using Microsoft.Win32;

namespace Islet.App.Settings;

/// <summary>
/// "Start with Windows", through the current user's <c>Run</c> registry key. That key needs no
/// admin rights and is exactly what Task Manager's Startup tab lists. Nothing is written until
/// the user turns the setting on.
/// </summary>
public sealed class StartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _keyPath;
    private readonly string _valueName;
    private readonly Func<string?> _exePath;

    public StartupRegistration(string? keyPath = null, string? valueName = null, Func<string?>? exePath = null)
    {
        _keyPath = keyPath ?? RunKeyPath;
        _valueName = valueName ?? IsletInfo.Name;
        _exePath = exePath ?? (() => Environment.ProcessPath);
    }

    /// <summary>True when a Run entry for the app exists.</summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_keyPath);
                return key?.GetValue(_valueName) is string;
            }
            catch (Exception exception)
            {
                FileLog.Write("Startup registry read failed", exception);
                return false;
            }
        }
    }

    /// <summary>The command Windows will run at sign-in: the quoted path of this exe.</summary>
    public string? Command => _exePath() is { Length: > 0 } path ? $"\"{path}\"" : null;

    /// <summary>Turns the Run entry on (pointing at this exe) or off. Returns false if it couldn't be changed.</summary>
    public bool Set(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
            if (enabled)
            {
                if (Command is not { } command)
                {
                    return false;
                }

                key.SetValue(_valueName, command, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(_valueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception exception)
        {
            FileLog.Write("Startup registry write failed", exception);
            return false;
        }
    }
}
