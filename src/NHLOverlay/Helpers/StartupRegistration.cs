using Microsoft.Win32;

namespace NHLOverlay.Helpers;

/// <summary>"Launch with Windows" via the per-user Run key (no administrator rights needed).</summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NHLOverlay";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled && Environment.ProcessPath is { } exe)
                key.SetValue(ValueName, $"\"{exe}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not update the Windows startup registration.", ex);
        }
    }
}
