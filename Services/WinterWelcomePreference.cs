using System;
using System.IO;

namespace CCSuitePro.Services;

/// <summary>
/// Optional decorative startup greeting. Isolated from settings.json, Mods,
/// saved scans, the portable distribution and any external service.
/// </summary>
internal static class WinterWelcomePreference
{
    private static readonly string PreferencePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCSuitePro", "Preferences", "winter.welcome");

    internal static bool ReadEnabled()
    {
        try
        {
            if (File.Exists(PreferencePath))
                return !string.Equals(File.ReadAllText(PreferencePath).Trim(), "off", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return true;
    }

    internal static void SaveEnabled(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PreferencePath)!);
            File.WriteAllText(PreferencePath, enabled ? "on" : "off");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
