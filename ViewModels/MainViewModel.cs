using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CCSuitePro.Models;
using CCSuitePro.Services;

namespace CCSuitePro.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ModScannerService _scannerService = new();
        private readonly DuplicateDetectorService _duplicateService = new();
        private readonly ConflictDetectorService _conflictService = new();
        private readonly OrganizerService _organizerService = new();
        private readonly BackupService _backupService = new();
        private readonly CleanerService _cleanerService = new();
        
        private ObservableCollection<ModFile> _allModFiles = new();
        private ObservableCollection<ModFile> _modFiles = new();
        private ObservableCollection<DuplicateGroup> _duplicateGroups = new();
        private ObservableCollection<ConflictResult> _conflicts = new();
        private ObservableCollection<CleanerService.CleanResult> _cleanResults = new();
        
        private int _totalFiles;
        private long _totalSize;
        private string _statusMessage = "Ready";
        private string _selectedFolder = "";
        private bool _isScanning;
        private double _progress;
        private string _searchText = "";
        private string _cleanStatus = "Ready to scan";
        private string _modsFolderPath = "";
        private string _cacheFolderPath = "";
        private int _selectedLanguageIndex = 0;
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        public int TotalFiles { get => _totalFiles; set { _totalFiles = value; OnPropertyChanged(); } }
        public long TotalSize { get => _totalSize; set { _totalSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalSizeFormatted)); } }
        public int DuplicateCount => _duplicateGroups.Count;
        public int ConflictCount => _conflicts.Count;
        public string TotalSizeFormatted { get { string[] s = {"B","KB","MB","GB"}; double l = _totalSize; int o = 0; while(l>=1024&&o<3){o++;l/=1024;} return $"{l:0.#} {s[o]}"; } }
        
        public ObservableCollection<ModFile> ModFiles { get => _modFiles; set { _modFiles = value; OnPropertyChanged(); } }
        public ObservableCollection<DuplicateGroup> DuplicateGroups { get => _duplicateGroups; set { _duplicateGroups = value; OnPropertyChanged(); } }
        public ObservableCollection<ConflictResult> Conflicts { get => _conflicts; set { _conflicts = value; OnPropertyChanged(); } }
        public ObservableCollection<CleanerService.CleanResult> CleanResults { get => _cleanResults; set { _cleanResults = value; OnPropertyChanged(); } }
        
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }
        public bool IsScanning { get => _isScanning; set { _isScanning = value; OnPropertyChanged(); } }
        public double Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }
        public string CleanStatus { get => _cleanStatus; set { _cleanStatus = value; OnPropertyChanged(); } }
        public string ModsFolderPath { get => _modsFolderPath; set { _modsFolderPath = value; OnPropertyChanged(); } }
        public string CacheFolderPath { get => _cacheFolderPath; set { _cacheFolderPath = value; OnPropertyChanged(); } }
        public string SearchText { get => _searchText; set { _searchText = value; OnPropertyChanged(); FilterFiles(); } }
        public int SelectedLanguageIndex { get => _selectedLanguageIndex; set { _selectedLanguageIndex = value; OnPropertyChanged(); } }
        public ObservableCollection<string> LanguageOptions { get; } = new ObservableCollection<string> { "English", "Espanol", "Ã­â€¢Å“ÃªÂµÂ­Ã¬â€“Â´" };
        
        public ICommand SelectFolderCommand { get; }
        public ICommand ScanCommand { get; }
        public ICommand OrganizeCommand { get; }
        public ICommand BackupCommand { get; }
        public ICommand ScanJunkCommand { get; }
        public ICommand CleanAllCommand { get; }
        public ICommand SelectModsFolderCommand { get; }
        public ICommand SelectCacheFolderCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand InstallModsCommand { get; }
        private bool _isPreviewLoaded;
        private string _organizePreview = "";
        
        public bool IsPreviewLoaded { get => _isPreviewLoaded; set { _isPreviewLoaded = value; OnPropertyChanged(); } }
        public string OrganizePreview { get => _organizePreview; set { _organizePreview = value; OnPropertyChanged(); } }
        public ICommand PreviewOrganizeCommand { get; }
        public ICommand ExecuteOrganizeCommand { get; }
        
        public MainViewModel()
        {
            LoadSettings();
            
            SelectFolderCommand = new RelayCommand(SelectFolder);
            ScanCommand = new RelayCommand(async () => await ScanAsync());
            OrganizeCommand = new RelayCommand(async () => await OrganizeAsync());
            BackupCommand = new RelayCommand(async () => await BackupAsync());
            ScanJunkCommand = new RelayCommand(async () => await ScanJunkAsync());
            CleanAllCommand = new RelayCommand(async () => await CleanAllAsync());
            SelectModsFolderCommand = new RelayCommand(SelectModsFolder);
            SelectCacheFolderCommand = new RelayCommand(SelectCacheFolder);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            InstallModsCommand = new RelayCommand(InstallMods);
            PreviewOrganizeCommand = new RelayCommand(PreviewOrganize);
            ExecuteOrganizeCommand = new RelayCommand(async () => await ExecuteOrganizeAsync());
        }
        
        private void LoadSettings()
        {
            SettingsService.Load();
            ModsFolderPath = SettingsService.Current.ModsFolderPath;
            CacheFolderPath = SettingsService.Current.CacheFolderPath;
            SelectedLanguageIndex = SettingsService.Current.Language switch { "es" => 1, "ko" => 2, _ => 0 };
            if (!string.IsNullOrEmpty(ModsFolderPath)) _selectedFolder = ModsFolderPath;
        }
        
        private void SelectFolder() { var d = new Microsoft.Win32.OpenFolderDialog{Title="Select Mods folder"}; if(d.ShowDialog()==true){_selectedFolder=d.FolderName;StatusMessage="Selected: "+_selectedFolder;_=ScanAsync();} }
        private async Task ScanAsync() { if(string.IsNullOrEmpty(_selectedFolder))return; IsScanning=true;Progress=0;_allModFiles.Clear();ModFiles.Clear(); try{var files=await _scannerService.ScanDirectoryAsync(_selectedFolder);foreach(var f in files)_allModFiles.Add(f);FilterFiles();RefreshStats();}catch(Exception ex){StatusMessage="Error: "+ex.Message;}finally{IsScanning=false;} }
        private void FilterFiles() { ModFiles.Clear(); var filtered = string.IsNullOrWhiteSpace(SearchText) ? _allModFiles.AsEnumerable() : _allModFiles.Where(f => f.FileName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)); foreach (var f in filtered) ModFiles.Add(f); }
        private void RefreshStats() { var files = _allModFiles.ToList(); TotalFiles = files.Count; TotalSize = files.Sum(f => f.FileSize); }
        private async Task OrganizeAsync() { if(_allModFiles.Count==0||string.IsNullOrEmpty(_selectedFolder))return; var r=MessageBox.Show("Organize?","Organize",MessageBoxButton.YesNo); if(r==MessageBoxResult.Yes){await _organizerService.OrganizeFilesAsync(_allModFiles.ToList(),_selectedFolder,false);await ScanAsync();} }
        private async Task BackupAsync() { if(string.IsNullOrEmpty(_selectedFolder))return; var bp=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"CC Suite Backups"); await _backupService.CreateFullBackupAsync(_selectedFolder,bp); MessageBox.Show("Backup done!","OK",MessageBoxButton.OK); }
        private async Task ScanJunkAsync() { if(string.IsNullOrEmpty(_selectedFolder))return; CleanStatus="Scanning...";CleanResults.Clear();var r=_cleanerService.ScanForJunk(_selectedFolder);foreach(var x in r)CleanResults.Add(x);CleanStatus="Found "+r.Sum(x=>x.FilesFound)+" junk files"; }
        private async Task CleanAllAsync() { if(string.IsNullOrEmpty(_selectedFolder))return; var r=MessageBox.Show("Delete all junk?","Clean",MessageBoxButton.YesNo); if(r!=MessageBoxResult.Yes)return; foreach(var x in CleanResults.ToList())_cleanerService.CleanCategory(_selectedFolder,x); CleanStatus="Cleaned!"; await ScanAsync(); }
        private void SelectModsFolder() { var d=new Microsoft.Win32.OpenFolderDialog{Title="Select Mods folder"}; if(d.ShowDialog()==true)ModsFolderPath=d.FolderName; }
        private void SelectCacheFolder() { var d=new Microsoft.Win32.OpenFolderDialog{Title="Select Cache folder"}; if(d.ShowDialog()==true)CacheFolderPath=d.FolderName; }
        
                
                
        private void PreviewOrganize()
        {
            if (_allModFiles.Count == 0 || string.IsNullOrEmpty(_selectedFolder))
            {
                OrganizePreview = "No files to organize. Scan a folder first.";
                IsPreviewLoaded = true;
                return;
            }
            
            var groups = _allModFiles.GroupBy(f => f.Category).OrderByDescending(g => g.Count());
            var preview = "";
            
            foreach (var group in groups)
            {
                var count = group.Count();
                var size = group.Sum(f => f.FileSize);
                var sizeStr = size switch
                {
                    > 1048576 => $"{size / 1048576.0:F1} MB",
                    > 1024 => $"{size / 1024.0:F1} KB",
                    _ => $"{size} B"
                };
                preview += $"{group.Key}: {count} files ({sizeStr})\n";
            }
            
            OrganizePreview = preview.Trim();
            IsPreviewLoaded = true;
            StatusMessage = $"Will organize {_allModFiles.Count} files into categories";
        }
        
        private async Task ExecuteOrganizeAsync()
        {
            if (_allModFiles.Count == 0 || string.IsNullOrEmpty(_selectedFolder)) return;
            
            var result = MessageBox.Show(
                "This will move all files into category folders.\n\nFolders: Clothing, Hair, Furniture, Build Mode, Scripts, etc.\n\nContinue?",
                "Organize Files", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                StatusMessage = "Organizing files...";
                await _organizerService.OrganizeFilesAsync(_allModFiles.ToList(), _selectedFolder, false);
                await ScanAsync();
                IsPreviewLoaded = false;
                OrganizePreview = "";
            }
        }

        private void InstallMods()
        {
            if (string.IsNullOrEmpty(_selectedFolder))
            {
                MessageBox.Show("Select your Mods folder first in Settings!", "No folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select .package or .ts4script files to install",
                Filter = "Sims 4 Mods (*.package;*.ts4script)|*.package;*.ts4script|All files (*.*)|*.*",
                Multiselect = true
            };
            
            if (dialog.ShowDialog() == true)
            {
                int installed = 0;
                int errors = 0;
                
                foreach (var file in dialog.FileNames)
                {
                    try
                    {
                        var fileName = Path.GetFileName(file);
                        var destPath = Path.Combine(_selectedFolder, fileName);
                        
                        // Evitar sobrescribir - agregar nÃƒÂºmero si existe
                        if (File.Exists(destPath))
                        {
                            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                            var ext = Path.GetExtension(fileName);
                            destPath = Path.Combine(_selectedFolder, $"{nameWithoutExt}_{DateTime.Now.Ticks}{ext}");
                        }
                        
                        File.Copy(file, destPath, true);
                        installed++;
                    }
                    catch { errors++; }
                }
                
                StatusMessage = $"Installed {installed} mods!";
                if (errors > 0) StatusMessage += $" ({errors} errors)";
                
                MessageBox.Show($"Successfully installed {installed} mod(s)!\n{errors} error(s)",
                    "Install Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Re-escanear
                _ = ScanAsync();
            }
        }

        private void SaveSettings()
        {
            if(!string.IsNullOrEmpty(ModsFolderPath)){_selectedFolder=ModsFolderPath;SettingsService.Current.ModsFolderPath=ModsFolderPath;}
            if(!string.IsNullOrEmpty(CacheFolderPath))SettingsService.Current.CacheFolderPath=CacheFolderPath;
            string lang=SelectedLanguageIndex switch{0=>"en",1=>"es",2=>"ko",_=>"en"};
            SettingsService.Current.Language=lang;
            SettingsService.Save();
            LanguageService.Instance.CurrentLanguage=lang;
            StatusMessage="Settings saved!";
            MessageBox.Show("Settings saved!\n\nRestart the app to apply language changes.","Settings",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object p) => true;
        public void Execute(object p) => _execute();
    }
}
