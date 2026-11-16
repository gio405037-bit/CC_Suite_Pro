using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CCSuitePro.Helpers;
using CCSuitePro.Models;

namespace CCSuitePro.Services
{
    public class ModScannerService
    {
        private readonly ClassificationEngine _classification = new();
        private readonly CreatorCatalogService _creatorCatalog = CreatorCatalogService.Load();
        private readonly PythonSuggestionService _python = new();
        public string AIStatus { get; private set; } = "AI not yet trained.";
        public event Action<string>? OnFileScanned;
        public event Action<int, int>? OnProgressChanged;
        public event Action<string>? OnScanCompleted;
        public event Action<string>? OnError;
        
        public async Task<List<ModFile>> ScanDirectoryAsync(string path)
        {
            var modFiles = new List<ModFile>();
            
            if (!Directory.Exists(path))
            {
                OnError?.Invoke($"Directory not found: {path}");
                return modFiles;
            }
            
            // Enumerate the folder only once and ignore inaccessible subfolders.
            var files = new List<string>();
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                files = Directory.EnumerateFiles(path, "*", options)
                    .Where(f => Path.GetExtension(f).Equals(".package", StringComparison.OrdinalIgnoreCase)
                             || Path.GetExtension(f).Equals(".ts4script", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Error reading Mods folder: {ex.Message}");
            }

            int total = files.Count;
            int current = 0;
            
            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    var classification = _classification.Classify(file);
                    var creatorMatch = _creatorCatalog.Identify(file);
                    var modFile = new ModFile
                    {
                        FilePath = file,
                        FileSize = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTime,
                        CreatorName = creatorMatch.Name,
                        CreatorEvidence = creatorMatch.Evidence,
                        Category = classification.PrimaryCategory,
                        ClassificationConfidence = classification.Confidence,
                        NeedsReview = classification.NeedsReview
                    };
                    
                    // Solo calcular MD5 para archivos grandes si es package/script
                    if (modFile.IsPackage || modFile.IsScript)
                    {
                        try
                        {
                            modFile.MD5Hash = await MD5Helper.CalculateMD5Async(file);
                        }
                        catch
                        {
                            modFile.MD5Hash = string.Empty; // Hash failure must never become a duplicate ID.
                        }
                    }
                    
                    modFiles.Add(modFile);
                    OnFileScanned?.Invoke(file);
                    current++;
                    
                    if (current % 100 == 0)
                    {
                        OnProgressChanged?.Invoke(current, total);
                    }
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Skip: {Path.GetFileName(file)} - {ex.Message}");
                }
            }
            
            // Optional and non-destructive, runs only once per scan (not once per file).
            AIStatus = await _python.ApplySuggestionsAsync(modFiles);
            OnProgressChanged?.Invoke(total, total);
            OnScanCompleted?.Invoke($"Scan complete: {modFiles.Count} files found");
            return modFiles;
        }
    }
}
