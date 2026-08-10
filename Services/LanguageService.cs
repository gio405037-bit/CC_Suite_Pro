using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace CCSuitePro.Services
{
    public class LanguageService : INotifyPropertyChanged
    {
        private static LanguageService _instance;
        public static LanguageService Instance => _instance ??= new LanguageService();
        
        private Dictionary<string, string> _strings = new();
        private string _currentLanguage = "en";
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        public string CurrentLanguage
        {
            get => _currentLanguage;
            set { _currentLanguage = value; LoadLanguage(value); OnPropertyChanged(); }
        }
        
        public string this[string key]
        {
            get => _strings.ContainsKey(key) ? _strings[key] : key;
        }
        
        private LanguageService()
        {
            LoadLanguage("en");
        }
        
        public void LoadLanguage(string lang)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages", $"{lang}.json");
            if (!File.Exists(path)) path = Path.Combine(Directory.GetCurrentDirectory(), "Languages", $"{lang}.json");
            if (!File.Exists(path)) return;
            
            var json = File.ReadAllText(path);
            _strings = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        
        public string Get(string key, params object[] args)
        {
            var text = this[key];
            if (args.Length > 0) text = string.Format(text, args);
            return text;
        }
        
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
