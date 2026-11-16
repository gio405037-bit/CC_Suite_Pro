using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CCSuitePro.Models;

namespace CCSuitePro.Services
{
    // Correctness-first change detection. A changed library uses the full safe scanner;
    // we must not retain deleted paths or lose creator/AI metadata in a partial refresh.
    public class SmartScanService
    {
        private readonly ModScannerService _scanner = new();
        private readonly ClassificationEngine _classifier = new();

        public event Action<string>? OnStatusChanged;
        public event Action<int, int>? OnProgressChanged;

        public SmartScanService()
        {
            _scanner.OnProgressChanged += (done, total) => OnProgressChanged?.Invoke(done, total);
        }

        public async Task<List<ModFile>> QuickScanAsync(string folderPath, List<ModFile> existingFiles)
        {
            if (!Directory.Exists(folderPath)) throw new DirectoryNotFoundException(folderPath);
            existingFiles ??= new List<ModFile>();
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            var discovered = Directory.EnumerateFiles(folderPath, "*", options)
                .Where(f => Path.GetExtension(f).Equals(".package", StringComparison.OrdinalIgnoreCase) ||
                            Path.GetExtension(f).Equals(".ts4script", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var previous = existingFiles.ToDictionary(f => f.FilePath, StringComparer.OrdinalIgnoreCase);
            bool changed = previous.Count != discovered.Count ||
                discovered.Any(file => !previous.TryGetValue(file, out var old) ||
                    new FileInfo(file).Length != old.FileSize ||
                    new FileInfo(file).LastWriteTimeUtc != old.LastModified.ToUniversalTime());

            if (!changed)
            {
                OnStatusChanged?.Invoke("No file changes detected; existing results retained.");
                OnProgressChanged?.Invoke(discovered.Count, discovered.Count);
                return existingFiles;
            }
            OnStatusChanged?.Invoke("Library changes detected. Refreshing all metadata to avoid stale results...");
            var refreshed = await _scanner.ScanDirectoryAsync(folderPath);
            OnStatusChanged?.Invoke($"Refresh complete: {refreshed.Count} files.");
            return refreshed;
        }

        public bool IsMisclassified(ModFile file)
        {
            if (file == null || !File.Exists(file.FilePath)) return false;
            var cls = _classifier.Classify(file.FilePath);
            return cls.PrimaryCategory != file.Category && cls.Confidence > 80;
        }

        public List<ModFile> FindMisclassified(List<ModFile> files)
        {
            var results = new List<ModFile>();
            for (int i = 0; i < files.Count; i++)
            {
                if (IsMisclassified(files[i])) results.Add(files[i]);
                OnProgressChanged?.Invoke(i + 1, files.Count);
            }
            OnStatusChanged?.Invoke($"Found {results.Count} potentially misclassified files.");
            return results;
        }
    }
}
