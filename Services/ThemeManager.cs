using System;
using System.IO;
using System.Windows;

namespace CCSuitePro.Services;

public enum ThemeVariant { Actual, NegroPremium, WinterEdition }

/// <summary>Only affects local UI resources. Does not touch Mods, scan cache or MD5.</summary>
public static class ThemeManager
{
    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCSuitePro", "Preferences", "appearance.theme");

    public static ThemeVariant Current { get; private set; } = ThemeVariant.Actual;

    public static void Initialize()
    {
        ThemeVariant saved = ThemeVariant.Actual;
        try
        {
            if (File.Exists(PreferencePath) &&
                string.Equals(File.ReadAllText(PreferencePath).Trim(), "NegroPremium", StringComparison.Ordinal))
                saved = ThemeVariant.NegroPremium;
            else if (File.Exists(PreferencePath) &&
                string.Equals(File.ReadAllText(PreferencePath).Trim(), "WinterEdition", StringComparison.Ordinal))
                saved = ThemeVariant.WinterEdition;
        }
        catch (IOException) { /* First-launch uses original appearance. */ }
        catch (UnauthorizedAccessException) { /* No settings can be read; leave original theme. */ }
        if (saved != ThemeVariant.Actual)
        {
            try { Apply(saved); }
            catch (Exception) { Current = ThemeVariant.Actual; }
        }
    }

    public static void ApplyAndSave(ThemeVariant selected)
    {
        Apply(selected);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
            File.WriteAllText(PreferencePath, selected switch
            {
                ThemeVariant.NegroPremium => "NegroPremium",
                ThemeVariant.WinterEdition => "WinterEdition",
                _ => "Actual"
            });
        }
        catch (IOException) { /* Theme remains active this session if persistence is unavailable. */ }
        catch (UnauthorizedAccessException) { /* Theme remains active this session. */ }
    }

    private static void Apply(ThemeVariant selected)
    {
        var resources = Application.Current?.Resources
            ?? throw new InvalidOperationException("WPF resources unavailable.");
        string name = selected switch
        {
            ThemeVariant.NegroPremium => "NegroPremium",
            ThemeVariant.WinterEdition => "WinterEdition",
            _ => "Actual"
        };
        // Load replacement fully before removing old resources, leaving original on failure.
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/CCSuitePro;component/Themes/{name}.xaml", UriKind.Absolute)
        };
        var merged = resources.MergedDictionaries;
        int oldIndex = -1;
        for (int i = 0; i < merged.Count; i++)
        {
            var uri = merged[i].Source?.ToString();
            if (uri != null && (uri.EndsWith("/Themes/Actual.xaml", StringComparison.OrdinalIgnoreCase) ||
                                uri.EndsWith("/Themes/NegroPremium.xaml", StringComparison.OrdinalIgnoreCase) ||
                                uri.EndsWith("/Themes/WinterEdition.xaml", StringComparison.OrdinalIgnoreCase) ||
                                uri.Equals("Themes/Actual.xaml", StringComparison.OrdinalIgnoreCase) ||
                                uri.Equals("Themes/NegroPremium.xaml", StringComparison.OrdinalIgnoreCase) ||
                                uri.Equals("Themes/WinterEdition.xaml", StringComparison.OrdinalIgnoreCase)))
            {
                oldIndex = i;
                break;
            }
        }
        if (oldIndex < 0) throw new InvalidOperationException("The application palette is missing.");
        merged[oldIndex] = replacement;
        Current = selected;
    }
}
