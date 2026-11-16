using System;
using System.Collections.Generic;
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
        private ObservableCollection<CleanerService.PackageIssue> _packageIssues = new();
        private bool _hasPackageScan;
        // Cached results are display-only until revalidated by a fresh scan.
        private bool _resultsAreCached;
        private bool _hasScanResult;
        private DateTimeOffset? _cachedScanAtUtc;
        private int _totalFiles;
        private long _totalSize;
        private string _statusMessage = LanguageService.Instance["status_ready"];
        private string _selectedFolder = "";
        private bool _isScanning;
        private double _progress;
        private string _searchText = "";
        private string _cleanStatus = "";
        private string _modsFolderPath = "";
        private string _cacheFolderPath = "";
        private int _selectedLanguageIndex;
        private bool _isPreviewLoaded;
        private bool _organizeByCreator;
        private string _aiStatus = LanguageService.Instance["ai_waiting"];
        private string _organizePreview = "";
        // Exact immutable preview snapshot. Execute only what the user reviewed.
        private IReadOnlyList<OrganizeMove> _pendingOrganizePlan = Array.Empty<OrganizeMove>();

        public event PropertyChangedEventHandler? PropertyChanged;
        public int TotalFiles { get => _totalFiles; private set { _totalFiles = value; OnPropertyChanged(); } }
        public long TotalSize { get => _totalSize; private set { _totalSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalSizeFormatted)); } }
        public int DuplicateCount => _duplicateGroups.Count;
        public int ConflictCount => _conflicts.Count;
        public string TotalSizeFormatted
        {
            get
            {
                string[] units = { "B", "KB", "MB", "GB", "TB" };
                double size = TotalSize; int i = 0;
                while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
                return $"{size:0.#} {units[i]}";
            }
        }
        public string ActiveFolderDisplay => string.IsNullOrWhiteSpace(_selectedFolder)
            ? LanguageService.Instance["no_folder"] : _selectedFolder;
        public bool HasLibrary => _allModFiles.Count != 0;
        public bool ResultsAreCached => _resultsAreCached;
        public ObservableCollection<ModFile> ModFiles { get => _modFiles; private set { _modFiles = value; OnPropertyChanged(); } }
        public ObservableCollection<DuplicateGroup> DuplicateGroups
        {
            get => _duplicateGroups;
            private set { _duplicateGroups = value; OnPropertyChanged(); OnPropertyChanged(nameof(DuplicateCount)); }
        }
        public ObservableCollection<ConflictResult> Conflicts
        {
            get => _conflicts;
            private set { _conflicts = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConflictCount)); }
        }
        public ObservableCollection<CleanerService.CleanResult> CleanResults
        {
            get => _cleanResults;
            private set { _cleanResults = value; OnPropertyChanged(); }
        }
        public ObservableCollection<CleanerService.PackageIssue> PackageIssues
        {
            get => _packageIssues;
            private set { _packageIssues = value; OnPropertyChanged(); OnPropertyChanged(nameof(PackageIssueCount)); }
        }
        public int PackageIssueCount => PackageIssues.Count;
        private bool HasSafeJunk => CleanResults.Any(r => r.Category == "Cache Files" || r.Category == "Temporary Files");
        public bool HasPackageScan
        {
            get => _hasPackageScan;
            private set { _hasPackageScan = value; OnPropertyChanged(); OnPropertyChanged(nameof(PackageScanMessage)); }
        }
        public string PackageScanMessage => !HasPackageScan
            ? LanguageService.Instance["package_scan_wait"]
            : PackageIssueCount == 0
                ? LanguageService.Instance["package_scan_clean"]
                : LanguageService.Instance.Get("package_scan_issues", PackageIssueCount);
        public string ScanStateLabel => LanguageService.Instance[IsScanning ? "scan_scanning" : "scan_standby"];
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }
        public bool IsScanning
        {
            get => _isScanning;
            private set { _isScanning = value; OnPropertyChanged(); OnPropertyChanged(nameof(ScanStateLabel)); CommandManager.InvalidateRequerySuggested(); }
        }
        public double Progress { get => _progress; private set { _progress = value; OnPropertyChanged(); } }
        public string CleanStatus { get => _cleanStatus; set { _cleanStatus = value; OnPropertyChanged(); } }
        public string ModsFolderPath { get => _modsFolderPath; set { _modsFolderPath = value; OnPropertyChanged(); } }
        public string CacheFolderPath { get => _cacheFolderPath; set { _cacheFolderPath = value; OnPropertyChanged(); } }
        public string SearchText { get => _searchText; set { _searchText = value; OnPropertyChanged(); FilterFiles(); } }
        public int SelectedLanguageIndex { get => _selectedLanguageIndex; set { _selectedLanguageIndex = value; OnPropertyChanged(); } }
        public ObservableCollection<string> LanguageOptions { get; } = new() { "English", "Español", "한국어" };
        public string AIStatus { get => _aiStatus; private set { _aiStatus = value; OnPropertyChanged(); } }
        public int AIReviewCount => _allModFiles.Count(f => f.NeedsReview);
        public ICommand ExportAIReviewCommand { get; }
        public ICommand QuarantineDuplicateGroupCommand { get; }
        public ICommand QuarantineDuplicateFileCommand { get; }
        public ICommand QuarantineCorruptedPackageCommand { get; }
        public bool OrganizeByCreator { get => _organizeByCreator; set { _organizeByCreator = value; OnPropertyChanged(); OrganizePreview = ""; IsPreviewLoaded = false; _pendingOrganizePlan = Array.Empty<OrganizeMove>(); } }
        public bool IsPreviewLoaded { get => _isPreviewLoaded; set { _isPreviewLoaded = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }
        public string OrganizePreview { get => _organizePreview; set { _organizePreview = value; OnPropertyChanged(); } }

        public ICommand SelectFolderCommand { get; }
        public ICommand ClearLibraryCommand { get; }
        public ICommand ScanCommand { get; }
        public ICommand OrganizeCommand { get; }
        public ICommand BackupCommand { get; }
        public ICommand ScanJunkCommand { get; }
        public ICommand CleanAllCommand { get; }
        public ICommand SelectModsFolderCommand { get; }
        public ICommand SelectCacheFolderCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand InstallModsCommand { get; }
        public ICommand PreviewOrganizeCommand { get; }
        public ICommand ExecuteOrganizeCommand { get; }

        public MainViewModel()
        {
            LoadSettings();
            // Native filename classifier is ready even before optional label training.
            AIStatus = PythonSuggestionService.InitialStatus;
            // Refresh view-model generated messages when the selected locale changes.
            LanguageService.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "Item[]")
                {
                    OnPropertyChanged(nameof(PackageScanMessage));
                    OnPropertyChanged(nameof(ScanStateLabel));
                    OnPropertyChanged(nameof(ActiveFolderDisplay));
                    // Rebuild only visible text. Stored scan entries, IDs and hashes stay untouched.
                    AIStatus = _hasScanResult
                        ? PythonSuggestionService.GetCurrentStatus(_allModFiles)
                        : PythonSuggestionService.InitialStatus;
                    if (_resultsAreCached && _cachedScanAtUtc.HasValue)
                        StatusMessage = CachedScanStatus();
                    FilterFiles(); // Re-evaluate presentation-only localized columns.
                }
            };
            _scannerService.OnProgressChanged += (done, total) =>
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    Progress = total == 0 ? 100 : 100.0 * done / total));
            };
            SelectFolderCommand = new RelayCommand(SelectFolder, () => !IsScanning);
            ClearLibraryCommand = new RelayCommand(ClearLibrary, () => !IsScanning);
            ScanCommand = new RelayCommand(async () => await ScanAsync(), () => !IsScanning && Directory.Exists(_selectedFolder));
            OrganizeCommand = new RelayCommand(PreviewOrganize, () => !IsScanning && HasLibrary && !_resultsAreCached);
            BackupCommand = new RelayCommand(async () => await BackupAsync(), () => !IsScanning && HasLibrary);
            ScanJunkCommand = new RelayCommand(async () => await ScanJunkAsync(), () => !IsScanning && Directory.Exists(_selectedFolder));
            CleanAllCommand = new RelayCommand(async () => await CleanAllAsync(), () => !IsScanning && HasSafeJunk);
            SelectModsFolderCommand = new RelayCommand(SelectModsFolder, () => !IsScanning);
            SelectCacheFolderCommand = new RelayCommand(SelectCacheFolder, () => !IsScanning);
            SaveSettingsCommand = new RelayCommand(SaveSettings, () => !IsScanning);
            InstallModsCommand = new RelayCommand(InstallMods, () => !IsScanning);
            PreviewOrganizeCommand = new RelayCommand(PreviewOrganize, () => !IsScanning && HasLibrary && !_resultsAreCached);
            ExecuteOrganizeCommand = new RelayCommand(async () => await ExecuteOrganizeAsync(), () => !IsScanning && HasLibrary && !_resultsAreCached && IsPreviewLoaded && _pendingOrganizePlan.Count > 0);
            ExportAIReviewCommand = new RelayCommand(ExportAIReviews, () => PythonSuggestionService.ReleaseAiEnabled && !IsScanning && HasLibrary);
            QuarantineDuplicateGroupCommand = new RelayCommand(
                async parameter => await QuarantineDuplicateGroupAsync(parameter as DuplicateGroup),
                parameter => !IsScanning && !_resultsAreCached && parameter is DuplicateGroup group && group.Files.Count > 1);
            QuarantineDuplicateFileCommand = new RelayCommand(
                async parameter => await QuarantineDuplicateFileAsync(parameter as ModFile),
                parameter => !IsScanning && !_resultsAreCached && parameter is ModFile file && DuplicateGroups.Any(g => g.Files.Any(f => string.Equals(f.FilePath, file.FilePath, StringComparison.OrdinalIgnoreCase))));
            QuarantineCorruptedPackageCommand = new RelayCommand(
                async parameter => await QuarantineCorruptedPackageAsync(parameter as CleanerService.PackageIssue),
                parameter => !IsScanning && parameter is CleanerService.PackageIssue issue && issue.CanQuarantine);
            CleanStatus = LanguageService.Instance["status_ready"];
            // Restore only if the saved snapshot belongs to the selected local folder.
            if (LastScanCacheService.TryLoad(_selectedFolder, out var stored))
            {
                ApplyScanResults(stored.Files, stored.DuplicateGroups, stored.Conflicts,
                    stored.AIStatus, fromCache: true);
                _cachedScanAtUtc = stored.ScannedAtUtc;
                StatusMessage = CachedScanStatus();
            }
        }

        private void LoadSettings()
        {
            SettingsService.Load();
            ModsFolderPath = SettingsService.Current.ModsFolderPath;
            CacheFolderPath = SettingsService.Current.CacheFolderPath;
            SelectedLanguageIndex = SettingsService.Current.Language switch { "es" => 1, "ko" => 2, _ => 0 };
            LanguageService.Instance.CurrentLanguage = SettingsService.Current.Language;
            if (Directory.Exists(ModsFolderPath)) _selectedFolder = ModsFolderPath;
        }

        private string CachedScanStatus() => LanguageService.Instance.Get(
            "scan_cached_loaded", _cachedScanAtUtc?.ToLocalTime().ToString("g") ?? "");

        private void ApplyScanResults(List<ModFile> files, List<DuplicateGroup> duplicates,
            List<ConflictResult> conflicts, string aiStatus, bool fromCache)
        {
            _allModFiles = new ObservableCollection<ModFile>(files);
            FilterFiles();
            DuplicateGroups = new ObservableCollection<DuplicateGroup>(duplicates);
            Conflicts = new ObservableCollection<ConflictResult>(conflicts);
            TotalFiles = files.Count;
            TotalSize = files.Sum(f => f.FileSize);
            AIStatus = fromCache ? PythonSuggestionService.GetCurrentStatus(files) : aiStatus;
            _hasScanResult = true;
            _resultsAreCached = fromCache;
            if (!fromCache) _cachedScanAtUtc = null;
            OnPropertyChanged(nameof(ResultsAreCached));
            OnPropertyChanged(nameof(AIReviewCount));
            OnPropertyChanged(nameof(HasLibrary));
            Progress = 100;
            // Cached results must not be used for quarantine or organization.
            IsPreviewLoaded = false;
            OrganizePreview = "";
            _pendingOrganizePlan = Array.Empty<OrganizeMove>();
            CommandManager.InvalidateRequerySuggested();
        }

        private void ResetLibraryResults()
        {
            // Resets ONLY the application view, never touches physical .package/.ts4script files.
            _resultsAreCached = false;
            _hasScanResult = false;
            _cachedScanAtUtc = null;
            OnPropertyChanged(nameof(ResultsAreCached));
            _allModFiles = new();
            ModFiles = new();
            DuplicateGroups = new();
            Conflicts = new();
            CleanResults = new();
            PackageIssues = new();
            HasPackageScan = false;
            TotalFiles = 0;
            TotalSize = 0;
            AIStatus = PythonSuggestionService.InitialStatus;
            OnPropertyChanged(nameof(AIReviewCount));
            Progress = 0;
            IsPreviewLoaded = false;
            OrganizePreview = "";
            _pendingOrganizePlan = Array.Empty<OrganizeMove>();
            OnPropertyChanged(nameof(HasLibrary));
            CommandManager.InvalidateRequerySuggested();
        }

        private void ClearLibrary()
        {
            // Clear also forgets the last persisted scan, never touches actual Mods.
            bool removed = LastScanCacheService.TryClear(out _);
            ResetLibraryResults();
            SearchText = "";
            StatusMessage = removed ? LanguageService.Instance["library_cleared"] :
                LanguageService.Instance["scan_cache_clear_failed"];
        }

        private void SelectFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = LanguageService.Instance["select_folder"] };
            if (dialog.ShowDialog() != true) return;
            if (!string.Equals(_selectedFolder, dialog.FolderName, StringComparison.OrdinalIgnoreCase))
                LastScanCacheService.TryClear(out _);
            _selectedFolder = dialog.FolderName;
            ModsFolderPath = _selectedFolder;
            SettingsService.Current.ModsFolderPath = _selectedFolder;
            SettingsService.Save();
            ResetLibraryResults(); // Other folders' results cannot be mixed into this session.
            OnPropertyChanged(nameof(ActiveFolderDisplay));
            StatusMessage = $"{LanguageService.Instance["selected_folder"]}: {_selectedFolder}";
            _ = ScanAsync();
        }

        private async Task ScanAsync()
        {
            if (IsScanning || !Directory.Exists(_selectedFolder)) return;
            string root = _selectedFolder;
            IsScanning = true;
            StatusMessage = LanguageService.Instance["scanning"];
            try
            {
                // Fresh results replace the previous list; they are never appended.
                var files = await Task.Run(() => _scannerService.ScanDirectoryAsync(root));
                var duplicates = await Task.Run(() => _duplicateService.FindDuplicates(files));
                var overlaps = await Task.Run(() => _conflictService.DetectConflicts(files));
                string aiStatus = _scannerService.AIStatus;
                ApplyScanResults(files, duplicates, overlaps, aiStatus, fromCache: false);
                HasPackageScan = false;
                PackageIssues = new();
                CleanResults = new();
                // Atomic overwrite of one user-local snapshot; no per-scan history.
                bool saved = await Task.Run(() => LastScanCacheService.TrySave(
                    root, files, duplicates, overlaps, aiStatus, out _));
                StatusMessage = saved ? LanguageService.Instance.Get("scan_complete", files.Count) :
                    LanguageService.Instance["scan_cache_save_failed"];
            }
            catch (Exception ex)
            {
                // Preserve last usable results if the next scan fails.
                StatusMessage = $"{LanguageService.Instance["error"]}: {ex.Message}";
            }
            finally { IsScanning = false; }
        }

        private void FilterFiles()
        {
            var filtered = string.IsNullOrWhiteSpace(SearchText) ? _allModFiles
                : _allModFiles.Where(f => f.FileName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || f.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                     || f.CreatorName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                     || f.AISuggestedCategory.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            ModFiles = new ObservableCollection<ModFile>(filtered);
        }

        private void PreviewOrganize()
        {
            if (_resultsAreCached || !HasLibrary || !Directory.Exists(_selectedFolder)) return;
            var plan = (OrganizeByCreator ? _organizerService.BuildCreatorPlan(_allModFiles, _selectedFolder) : _organizerService.BuildPlan(_allModFiles, _selectedFolder));
            _pendingOrganizePlan = plan.ToArray();
            OrganizePreview = plan.Count == 0
                ? LanguageService.Instance["no_moves"]
                : LanguageService.Instance.Get("preview_moves", plan.Count) + Environment.NewLine
                    + string.Join(Environment.NewLine, plan.Take(30).Select(p =>
                        $"• {Path.GetRelativePath(_selectedFolder, p.Source)} → {Path.GetRelativePath(_selectedFolder, p.Destination)}"))
                    + (plan.Count > 30 ? Environment.NewLine + $"… (+{plan.Count - 30})" : "");
            IsPreviewLoaded = true;
            CommandManager.InvalidateRequerySuggested();
            StatusMessage = LanguageService.Instance.Get("preview_moves", plan.Count);
        }

        private async Task OrganizeAsync() { PreviewOrganize(); await Task.CompletedTask; }

        private async Task ExecuteOrganizeAsync()
        {
            if (_resultsAreCached || !HasLibrary || IsScanning) return;
            if (!IsPreviewLoaded)
            {
                PreviewOrganize();
                MessageBox.Show(LanguageService.Instance["organize_preview_first"], "CC Suite Pro", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // Use the exact reviewed move plan; NEVER regenerate it at execution time.
            // OrganizerService independently refuses any source changed since preview.
            var plan = _pendingOrganizePlan;
            if (plan.Count == 0) { PreviewOrganize(); return; }
            var yes = MessageBox.Show(LanguageService.Instance.Get("confirm_organize", plan.Count),
                LanguageService.Instance["organize"], MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (yes != MessageBoxResult.Yes) return;
            IsScanning = true;
            try
            {
                // Required and verified before any actual move.
                StatusMessage = LanguageService.Instance["backup_progress"];
                var backups = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CC Suite Backups");
                await _backupService.CreateFullBackupAsync(_selectedFolder, backups);
                StatusMessage = LanguageService.Instance["organizing"];
                await _organizerService.ExecutePlanAsync(plan);
                StatusMessage = LanguageService.Instance.Get("moved", plan.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, LanguageService.Instance["error"], MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { IsScanning = false; }
            await ScanAsync();
        }

        private static string Csv(string text) => "\"" + (text ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private static List<string> ParseCsvRow(string line)
        {
            var fields = new List<string>();
            var current = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '\"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '\"')
                    {
                        current.Append('\"');
                        i++;
                    }
                    else quoted = !quoted;
                }
                else if (ch == ',' && !quoted)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else current.Append(ch);
            }
            fields.Add(current.ToString());
            return fields;
        }

        private void ExportAIReviews()
        {
            try
            {
                var location = PythonSuggestionService.LabelsPath;
                Directory.CreateDirectory(Path.GetDirectoryName(location)!);
                const string header = "FileName,CurrentCategory,ConfirmedCategory,Confidence,NeedsReview";
                var rows = new Dictionary<string, (string Current, string Confirmed, string Confidence, string NeedsReview)>(StringComparer.OrdinalIgnoreCase);

                if (File.Exists(location))
                {
                    var lines = File.ReadAllLines(location, System.Text.Encoding.UTF8);
                    if (lines.Length > 0)
                    {
                        var columns = ParseCsvRow(lines[0]);
                        int nameIndex = columns.FindIndex(c => c.Equals("FileName", StringComparison.OrdinalIgnoreCase));
                        int currentIndex = columns.FindIndex(c => c.Equals("CurrentCategory", StringComparison.OrdinalIgnoreCase));
                        int confirmedIndex = columns.FindIndex(c => c.Equals("ConfirmedCategory", StringComparison.OrdinalIgnoreCase));
                        int confidenceIndex = columns.FindIndex(c => c.Equals("Confidence", StringComparison.OrdinalIgnoreCase));
                        int reviewIndex = columns.FindIndex(c => c.Equals("NeedsReview", StringComparison.OrdinalIgnoreCase));
                        if (nameIndex < 0) throw new InvalidDataException(LanguageService.Instance["dialog_ai_confirm_labels"]);
                        foreach (var line in lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)))
                        {
                            var fields = ParseCsvRow(line);
                            string Get(int index) => index >= 0 && index < fields.Count ? fields[index] : string.Empty;
                            string name = Get(nameIndex).Trim();
                            if (name.Length == 0) continue;
                            rows[name] = (Get(currentIndex), Get(confirmedIndex), Get(confidenceIndex), Get(reviewIndex));
                        }
                    }
                }

                // Refresh current suggestions but never overwrite the user's ConfirmedCategory.
                foreach (var file in _allModFiles)
                {
                    string name = file.FileName;
                    rows.TryGetValue(name, out var previous);
                    rows[name] = (file.Category, previous.Confirmed ?? string.Empty,
                        file.ClassificationConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        file.NeedsReview ? "true" : "false");
                }

                string temporary = location + ".tmp";
                using (var writer = new StreamWriter(temporary, false, new System.Text.UTF8Encoding(true)))
                {
                    writer.WriteLine(header);
                    foreach (var row in rows.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                        writer.WriteLine(string.Join(",", Csv(row.Key), Csv(row.Value.Current), Csv(row.Value.Confirmed),
                            Csv(row.Value.Confidence), Csv(row.Value.NeedsReview)));
                }
                File.Move(temporary, location, true);
                StatusMessage = LanguageService.Instance.Get("ai_csv_export_success", location);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(location) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, LanguageService.Instance["ai_csv_export_title"], MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task QuarantineDuplicateGroupAsync(DuplicateGroup? group)
        {
            if (_resultsAreCached || group == null || IsScanning || string.IsNullOrWhiteSpace(_selectedFolder)) return;
            int candidates = group.Files.Count(f => File.Exists(f.FilePath));
            if (candidates < 2) { StatusMessage = LanguageService.Instance["duplicate_group_stale"]; return; }
            string prompt = LanguageService.Instance.Get("quarantine_group_confirm", candidates - 1);
            if (MessageBox.Show(prompt, LanguageService.Instance["quarantine_group_title"], MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            IsScanning = true;
            QuarantineActionResult result;
            try { result = await Task.Run(() => _duplicateService.QuarantineDuplicateCopies(_selectedFolder, group, keepNewest: true)); }
            finally { IsScanning = false; }
            StatusMessage = result.Message;
            MessageBox.Show(result.Message, LanguageService.Instance["duplicate_cleanup_title"], MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (result.Success) await ScanAsync();
        }

        private async Task QuarantineDuplicateFileAsync(ModFile? file)
        {
            if (_resultsAreCached || file == null || IsScanning || string.IsNullOrWhiteSpace(_selectedFolder)) return;
            var group = DuplicateGroups.FirstOrDefault(g => g.Files.Any(f =>
                string.Equals(f.FilePath, file.FilePath, StringComparison.OrdinalIgnoreCase)));
            if (group == null || group.Files.Count(f => File.Exists(f.FilePath)) < 2)
            {
                StatusMessage = LanguageService.Instance["duplicate_no_copy"];
                return;
            }
            string prompt = LanguageService.Instance.Get("quarantine_one_confirm", file.FileName);
            if (MessageBox.Show(prompt, LanguageService.Instance["quarantine_one_title"], MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            IsScanning = true;
            QuarantineActionResult result;
            try { result = await Task.Run(() => _duplicateService.QuarantineDuplicateFile(_selectedFolder, group, file)); }
            finally { IsScanning = false; }
            StatusMessage = result.Message;
            MessageBox.Show(result.Message, LanguageService.Instance["duplicate_cleanup_title"], MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (result.Success) await ScanAsync();
        }

        private async Task QuarantineCorruptedPackageAsync(CleanerService.PackageIssue? issue)
        {
            if (issue == null || !issue.CanQuarantine || IsScanning || string.IsNullOrWhiteSpace(_selectedFolder)) return;
            string prompt = LanguageService.Instance.Get("quarantine_corrupted_confirm", issue.FileName, issue.Issue);
            if (MessageBox.Show(prompt, LanguageService.Instance["quarantine_corrupted_title"], MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            IsScanning = true;
            QuarantineActionResult result;
            try { result = await Task.Run(() => _cleanerService.QuarantineCorruptedPackage(_selectedFolder, issue)); }
            finally { IsScanning = false; }
            StatusMessage = result.Message;
            MessageBox.Show(result.Message, LanguageService.Instance["package_integrity_title"], MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (result.Success) await ScanJunkAsync();
        }

        private async Task BackupAsync()
        {
            if (IsScanning || !Directory.Exists(_selectedFolder)) return;
            IsScanning = true;
            try
            {
                StatusMessage = LanguageService.Instance["backup_progress"];
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CC Suite Backups");
                await _backupService.CreateFullBackupAsync(_selectedFolder, folder);
                StatusMessage = LanguageService.Instance["backup_complete"];
                MessageBox.Show(StatusMessage, LanguageService.Instance["backup"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, LanguageService.Instance["error"], MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { IsScanning = false; }
        }

        private async Task ScanJunkAsync()
        {
            if (!Directory.Exists(_selectedFolder) || IsScanning) return;
            IsScanning = true;
            HasPackageScan = false;
            PackageIssues = new();
            try
            {
                CleanStatus = LanguageService.Instance["scanning"];
                var list = await Task.Run(() => _cleanerService.ScanForJunk(_selectedFolder));
                CleanResults = new ObservableCollection<CleanerService.CleanResult>(list);
                PackageIssues = new ObservableCollection<CleanerService.PackageIssue>(list.SelectMany(r => r.Items));
                HasPackageScan = true;
                CleanStatus = LanguageService.Instance.Get("junk_found", list.Sum(r => r.FilesFound));
            }
            catch (Exception ex) { CleanStatus = ex.Message; }
            finally { IsScanning = false; CommandManager.InvalidateRequerySuggested(); }
        }

        private async Task CleanAllAsync()
        {
            if (IsScanning || !HasSafeJunk) return;
            if (MessageBox.Show(LanguageService.Instance["confirm_clean"], LanguageService.Instance["cleaner"],
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            IsScanning = true;
            try
            {
                var backups = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CC Suite Backups");
                await _backupService.CreateFullBackupAsync(_selectedFolder, backups);
                var results = CleanResults.ToList();
                await Task.Run(() => { foreach (var r in results) _cleanerService.CleanCategory(_selectedFolder, r); });
                CleanStatus = LanguageService.Instance["clean_complete"];
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, LanguageService.Instance["error"], MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { IsScanning = false; }
            await ScanAsync();
        }

        private void SelectModsFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = LanguageService.Instance["select_folder"] };
            if (dialog.ShowDialog() == true) ModsFolderPath = dialog.FolderName;
        }
        private void SelectCacheFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = LanguageService.Instance["cache_folder"] };
            if (dialog.ShowDialog() == true) CacheFolderPath = dialog.FolderName;
        }
        private void InstallMods()
        {
            if (!Directory.Exists(_selectedFolder))
            {
                MessageBox.Show(LanguageService.Instance["no_folder"], LanguageService.Instance["select_folder"]);
                return;
            }
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = LanguageService.Instance["add_mods"],
                Filter = "Sims 4 Mods (*.package;*.ts4script)|*.package;*.ts4script",
                Multiselect = true
            };
            if (dialog.ShowDialog() != true) return;
            int installed = 0, skipped = 0, errors = 0;
            foreach (var source in dialog.FileNames)
            {
                try
                {
                    var target = Path.Combine(_selectedFolder, Path.GetFileName(source));
                    if (File.Exists(target)) { skipped++; continue; } // Keep original filenames/identifiers unchanged.
                    File.Copy(source, target, false);
                    installed++;
                }
                catch { errors++; }
            }
            StatusMessage = LanguageService.Instance.Get("installed_summary", installed, skipped, errors);
            MessageBox.Show(StatusMessage, LanguageService.Instance["add_mods"]);
            _ = ScanAsync();
        }
        private void SaveSettings()
        {
            if (!string.IsNullOrWhiteSpace(ModsFolderPath) && Directory.Exists(ModsFolderPath))
            {
                if (!string.Equals(_selectedFolder, ModsFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    LastScanCacheService.TryClear(out _);
                    _selectedFolder = ModsFolderPath;
                    ResetLibraryResults();
                    OnPropertyChanged(nameof(ActiveFolderDisplay));
                }
                SettingsService.Current.ModsFolderPath = ModsFolderPath;
            }
            SettingsService.Current.CacheFolderPath = CacheFolderPath;
            string lang = SelectedLanguageIndex switch { 1 => "es", 2 => "ko", _ => "en" };
            SettingsService.Current.Language = lang;
            SettingsService.Save();
            LanguageService.Instance.CurrentLanguage = lang; // Live UI update, no restart.
            OnPropertyChanged(nameof(ActiveFolderDisplay));
            StatusMessage = LanguageService.Instance["settings_saved"];
            CleanStatus = LanguageService.Instance["status_ready"];
        }
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool> _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = _ => execute();
            _canExecute = _ => canExecute?.Invoke() ?? true;
        }

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute ?? (_ => true);
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);
    }
}
