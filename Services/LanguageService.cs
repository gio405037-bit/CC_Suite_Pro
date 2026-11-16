using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace CCSuitePro.Services
{
    public class LanguageService : INotifyPropertyChanged
    {
        private static LanguageService? _instance;
        public static LanguageService Instance => _instance ??= new LanguageService();
        private Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _english = new(StringComparer.OrdinalIgnoreCase);
        private string _currentLanguage = "en";
        public event PropertyChangedEventHandler? PropertyChanged;
        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                var next = value is "en" or "es" or "ko" ? value : "en";
                if (_currentLanguage == next && _strings.Count > 0) return;
                _currentLanguage = next;
                LoadLanguage(next); // LoadLanguage notifies all WPF indexer bindings.
            }
        }
        public string this[string key] => _strings.TryGetValue(key, out var text)
            ? text : _english.TryGetValue(key, out var fallback) ? fallback : key;
        private LanguageService() { _english = ReadLanguage("en"); _strings = _english; }
        private static Dictionary<string, string> ReadLanguage(string lang)
        {
            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages", $"{lang}.json");
            if (!File.Exists(file)) file = Path.Combine(Directory.GetCurrentDirectory(), "Languages", $"{lang}.json");
            try { return File.Exists(file)
                ? JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(file)) ?? new()
                : new(); }
            catch { return new(); }
        }
        public void LoadLanguage(string lang)
        {
            _currentLanguage = lang is "en" or "es" or "ko" ? lang : "en";
            _english = ReadLanguage("en");
            _strings = _currentLanguage == "en" ? _english : ReadLanguage(_currentLanguage);
            OnPropertyChanged(nameof(CurrentLanguage));
            OnPropertyChanged("Item[]");
        }
        public string Get(string key, params object[] args)
        {
            string value = this[key];
            return args.Length == 0 ? value : string.Format(System.Globalization.CultureInfo.CurrentCulture, value, args);
        }
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
