using Microsoft.Win32;

namespace PowerMate.Services;

/// <summary>
/// Windows autostart registration.
///
/// The HKCU Run value is the single source of truth: the installer writes the
/// same value, so the installer checkbox and the in-app toggle can never
/// disagree, and either can undo the other.
/// </summary>
public static class StartupService
{
    private const string KeyPath   = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PowerMateDriver";

    // Installers up to 1.4.6 registered autostart with a Startup-folder shortcut
    // instead, which the app could neither see nor remove.
    private static string LegacyShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "PowerMate Driver.lnk");

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
        return key?.GetValue(ValueName) != null;
    }

    /// <summary>Throws if the Run key cannot be written; callers surface the failure.</summary>
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (enabled) key.SetValue(ValueName, QuotedExePath());
        else         key.DeleteValue(ValueName, throwOnMissingValue: false);

        RemoveLegacyShortcut();
    }

    /// <summary>
    /// Converts a pre-1.4.7 Startup-folder shortcut into the Run value so an
    /// existing autostart choice survives the upgrade, then removes the shortcut
    /// so the app never launches twice.
    /// </summary>
    public static void MigrateLegacyStartup()
    {
        if (!File.Exists(LegacyShortcutPath)) return;

        if (IsEnabled()) RemoveLegacyShortcut(); // Run value already wins; drop the duplicate
        else             Set(true);              // shortcut was the only autostart; preserve it
    }

    private static void RemoveLegacyShortcut()
    {
        try { File.Delete(LegacyShortcutPath); }
        catch { /* best effort — a stale shortcut is not worth failing the toggle over */ }
    }

    // The Run key's command parser splits an unquoted path on spaces, and the
    // default install directory lives under Program Files.
    private static string QuotedExePath()
    {
        var path = Environment.ProcessPath;
        return string.IsNullOrEmpty(path) ? "" : $"\"{path}\"";
    }
}
