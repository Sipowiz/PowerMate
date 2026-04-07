using Microsoft.Win32;

namespace PowerMate.Services;

public static class StartupService
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PowerMateDriver";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
        return key?.GetValue(ValueName) != null;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
        if (enabled)
            key?.SetValue(ValueName, Environment.ProcessPath ?? "");
        else
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
