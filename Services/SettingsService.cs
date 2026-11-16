using System;
using System.IO;
using Newtonsoft.Json;

namespace CCSuitePro.Services
{
    public class AppSettingsData
    {
        public string Language { get; set; } = "en";
        public string ModsFolderPath { get; set; } = "";
        public string CacheFolderPath { get; set; } = "";
    }
    
    public static class SettingsService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CCSuitePro", "settings.json");
        
        private static AppSettingsData _current = new();
        
        public static AppSettingsData Current => _current;
        
        public static void Load()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    _current = JsonConvert.DeserializeObject<AppSettingsData>(json) ?? new AppSettingsData();
                }
            }
            catch { _current = new AppSettingsData(); }
        }
        
        public static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                
                var json = JsonConvert.SerializeObject(_current, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}
