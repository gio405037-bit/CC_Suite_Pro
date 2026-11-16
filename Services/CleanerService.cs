using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace CCSuitePro.Services
{
    // Release hardening: never delete a Sims mod, and never infer corruption from access errors.
    public class CleanerService
    {
        private readonly string _quarantineRoot;
        public CleanerService(string? quarantineRoot = null)
        {
            _quarantineRoot = quarantineRoot ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CCSuitePro", "Quarantine");
        }

        public event Action<string>? OnStatusChanged;
        public event Action<int, int>? OnProgressChanged;
        public event Action<string, int>? OnItemCleaned;

        public class CleanResult
        {
            public string Category { get; set; } = "";
            public int FilesFound { get; set; }
            // Legacy UI property: counts items moved to quarantine, NOT permanently deleted.
            public int FilesDeleted { get; set; }
            public long SpaceFreed { get; set; }
            public List<PackageIssue> Items { get; set; } = new();
            public string SpaceFreedFormatted
            {
                get
                {
                    string[] sizes = { "B", "KB", "MB", "GB" };
                    double len = SpaceFreed;
                    int order = 0;
                    while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
                    return $"{len:0.#} {sizes[order]}";
                }
            }
        }

        public sealed class PackageIssue
        {
            public string FilePath { get; set; } = string.Empty;
            public string Issue { get; set; } = string.Empty;
            public long FileSize { get; set; }
            public bool CanQuarantine { get; set; }
            public string FileName => Path.GetFileName(FilePath);
            public string FileSizeFormatted
            {
                get
                {
                    string[] units = { "B", "KB", "MB", "GB" };
                    double size = FileSize;
                    int i = 0;
                    while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
                    return $"{size:0.#} {units[i]}";
                }
            }
        }

        private enum PackageCheckState { Valid, Invalid, Unreadable }

        private static readonly HashSet<string> CacheExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".cache", ".cached", ".cacheitem" };

        private static readonly HashSet<string> ModExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".package", ".ts4script" };

        private static IEnumerable<string> EnumerateFiles(string root)
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            return Directory.EnumerateFiles(root, "*", options);
        }

        private static bool IsSafeFile(string root, string file)
        {
            var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(file);
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (ModExtensions.Contains(Path.GetExtension(full))) return false;
            try { return (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0; }
            catch { return false; }
        }

        private static List<string> GetCandidates(string root, string category)
        {
            if (!Directory.Exists(root)) return new List<string>();
            var result = new List<string>();
            foreach (var file in EnumerateFiles(root))
            {
                if (!IsSafeFile(root, file)) continue;
                var extension = Path.GetExtension(file);
                bool include = category switch
                {
                    "Cache Files" => CacheExtensions.Contains(extension),
                    "Temporary Files" => extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase),
                    // Empty Sims packages, scripts and configuration files are NOT auto-cleaned.
                    "Empty Files" => !CacheExtensions.Contains(extension) &&
                                     !extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) &&
                                     SafeLength(file) == 0,
                    _ => false
                };
                if (include) result.Add(file);
            }
            return result;
        }

        private static long SafeLength(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return -1; }
        }

        public List<CleanResult> ScanForJunk(string modsFolder)
        {
            var result = new List<CleanResult>();
            if (!Directory.Exists(modsFolder))
            {
                OnStatusChanged?.Invoke("Folder not found!");
                return result;
            }
            foreach (var name in new[] { "Cache Files", "Temporary Files", "Empty Files" })
            {
                var files = GetCandidates(modsFolder, name);
                if (files.Count == 0) continue;
                result.Add(new CleanResult
                {
                    Category = name,
                    FilesFound = files.Count,
                    SpaceFreed = files.Sum(SafeLength)
                });
            }
            // A Sims 4 .package must have a complete DBPF header. Access errors are
            // reported separately and are never treated as corruption.
            var corrupted = new List<PackageIssue>();
            var unreadable = new List<PackageIssue>();
            foreach (var file in EnumerateFiles(modsFolder)
                .Where(f => Path.GetExtension(f).Equals(".package", StringComparison.OrdinalIgnoreCase)))
            {
                var state = InspectPackage(file, out string reason, out long size);
                if (state == PackageCheckState.Invalid)
                    corrupted.Add(new PackageIssue { FilePath = file, Issue = reason, FileSize = size, CanQuarantine = true });
                else if (state == PackageCheckState.Unreadable)
                    unreadable.Add(new PackageIssue { FilePath = file, Issue = reason, FileSize = size, CanQuarantine = false });
            }
            if (corrupted.Count > 0)
                result.Add(new CleanResult { Category = "Corrupted Files", FilesFound = corrupted.Count, Items = corrupted });
            if (unreadable.Count > 0)
                result.Add(new CleanResult { Category = "Unreadable Packages", FilesFound = unreadable.Count, Items = unreadable });
            return result;
        }

        private static PackageCheckState InspectPackage(string file, out string reason, out long size)
        {
            reason = string.Empty;
            size = 0;
            try
            {
                var info = new FileInfo(file);
                size = info.Length;
                // DBPF headers are 96 bytes. A shorter file is a truncated package.
                if (size < 96)
                {
                    reason = $"Truncated DBPF header ({size} bytes; expected at least 96).";
                    return PackageCheckState.Invalid;
                }
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                Span<byte> signature = stackalloc byte[4];
                stream.ReadExactly(signature);
                if (!signature.SequenceEqual("DBPF"u8))
                {
                    reason = "Invalid DBPF signature. This is not a readable Sims 4 package.";
                    return PackageCheckState.Invalid;
                }
                return PackageCheckState.Valid;
            }
            catch (Exception ex)
            {
                reason = "Cannot read file: " + ex.Message;
                try { size = new FileInfo(file).Length; } catch { size = 0; }
                return PackageCheckState.Unreadable;
            }
        }

        private static bool IsInsideRoot(string root, string file)
        {
            try
            {
                string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(file);
                return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public QuarantineActionResult QuarantineCorruptedPackage(string modsFolder, PackageIssue issue)
        {
            if (issue == null || !issue.CanQuarantine)
                return new QuarantineActionResult { Message = "This item is not eligible for automatic quarantine." };
            if (!Directory.Exists(modsFolder) || !IsInsideRoot(modsFolder, issue.FilePath))
                return new QuarantineActionResult { Message = "The file is outside the selected Mods folder." };
            if (!Path.GetExtension(issue.FilePath).Equals(".package", StringComparison.OrdinalIgnoreCase) || !File.Exists(issue.FilePath))
                return new QuarantineActionResult { Message = "The package no longer exists or is not a .package file." };

            try
            {
                if ((File.GetAttributes(issue.FilePath) & FileAttributes.ReparsePoint) != 0)
                    return new QuarantineActionResult { Message = "Symbolic links and reparse points are not moved." };
                var state = InspectPackage(issue.FilePath, out string currentReason, out long currentSize);
                if (state != PackageCheckState.Invalid)
                    return new QuarantineActionResult { Message = state == PackageCheckState.Valid
                        ? "The package now has a valid DBPF header. It was left untouched."
                        : "The package is unreadable. Access errors are not treated as corruption." };

                string sha256;
                using (var input = File.OpenRead(issue.FilePath))
                    sha256 = Convert.ToHexString(SHA256.HashData(input));
                // Re-check after hashing so a package repaired since the first check is not moved.
                var finalState = InspectPackage(issue.FilePath, out currentReason, out currentSize);
                if (finalState != PackageCheckState.Invalid)
                    return new QuarantineActionResult { Message = "The package changed after inspection and was left untouched." };
                string quarantine = Path.Combine(_quarantineRoot,
                    "Corrupted_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(quarantine);
                string destination = Path.Combine(quarantine, Guid.NewGuid().ToString("N") + "_" + Path.GetFileName(issue.FilePath));
                string manifestPath = Path.Combine(quarantine, "manifest.json");
                try
                {
                    // Copy first for cross-volume safety. Remove the source only after
                    // verifying the copied bytes and writing the recovery manifest.
                    File.Copy(issue.FilePath, destination, false);
                    using var moved = File.OpenRead(destination);
                    string movedHash = Convert.ToHexString(SHA256.HashData(moved));
                    if (!string.Equals(sha256, movedHash, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("SHA-256 changed during quarantine verification.");
                    var manifest = new
                    {
                        originalPath = issue.FilePath,
                        quarantinedPath = destination,
                        reason = currentReason,
                        size = currentSize,
                        sha256,
                        movedAtUtc = DateTime.UtcNow
                    };
                    File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                    File.Delete(issue.FilePath);
                }
                catch
                {
                    try { if (File.Exists(issue.FilePath) && File.Exists(destination)) File.Delete(destination); } catch { }
                    try { if (File.Exists(manifestPath)) File.Delete(manifestPath); } catch { }
                    try { if (Directory.Exists(quarantine) && !Directory.EnumerateFileSystemEntries(quarantine).Any()) Directory.Delete(quarantine); } catch { }
                    throw;
                }
                OnStatusChanged?.Invoke($"Quarantined corrupted package: {issue.FileName}");
                return new QuarantineActionResult
                {
                    Success = true,
                    FilesQuarantined = 1,
                    QuarantinePath = destination,
                    Message = $"Moved {issue.FileName} to quarantine: {destination}. Original bytes verified with SHA-256."
                };
            }
            catch (Exception ex)
            {
                return new QuarantineActionResult { Message = "Quarantine failed: " + ex.Message };
            }
        }

        public CleanResult CleanCategory(string modsFolder, CleanResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (!Directory.Exists(modsFolder)) throw new DirectoryNotFoundException(modsFolder);
            if (result.Category == "Corrupted Files" || result.Category == "Empty Files" || result.Category == "Unreadable Packages")
            {
                // Never remove suspicious/empty mods or arbitrary user files automatically.
                result.FilesDeleted = 0;
                OnStatusChanged?.Invoke($"{result.Category}: manual review only. No files moved.");
                return result;
            }
            if (result.Category != "Cache Files" && result.Category != "Temporary Files")
                return result;

            var root = Path.GetFullPath(modsFolder);
            var candidates = GetCandidates(root, result.Category);
            if (candidates.Count == 0) return result;
            var quarantineBase = Path.Combine(_quarantineRoot,
                "Cleaner_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N"));
            var quarantined = new List<(string Original, string Saved)>();
            try
            {
                Directory.CreateDirectory(quarantineBase);
                var manifest = Path.Combine(quarantineBase, "manifest.jsonl");
                for (int i = 0; i < candidates.Count; i++)
                {
                    var source = candidates[i];
                    if (!IsSafeFile(root, source) || !File.Exists(source)) continue;
                    var relative = Path.GetRelativePath(root, source);
                    var destination = Path.Combine(quarantineBase, "files", relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    // Refuse to overwrite any other quarantined item.
                    if (File.Exists(destination)) continue;
                    File.Move(source, destination);
                    quarantined.Add((source, destination));
                    File.AppendAllText(manifest, JsonSerializer.Serialize(new { original = source, quarantined = destination }) + Environment.NewLine);
                    OnItemCleaned?.Invoke(source, 1);
                    OnProgressChanged?.Invoke(i + 1, candidates.Count);
                }
            }
            catch (Exception ex)
            {
                // Restore this category's changes if writing the journal or moving a file fails.
                var failedRestores = new List<string>();
                foreach (var item in quarantined.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (File.Exists(item.Saved) && !File.Exists(item.Original))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(item.Original)!);
                            File.Move(item.Saved, item.Original);
                        }
                    }
                    catch { failedRestores.Add(item.Saved); }
                }
                if (failedRestores.Count > 0)
                    throw new IOException($"Quarantine failed. Manual restoration needed for: {string.Join(", ", failedRestores)}", ex);
                throw new IOException("Quarantine failed; moved files were restored.", ex);
            }
            result.FilesDeleted = quarantined.Count;
            result.SpaceFreed = quarantined.Sum(p => SafeLength(p.Saved));
            OnStatusChanged?.Invoke($"Quarantined {quarantined.Count} {result.Category} item(s). Restore from: {quarantineBase}");
            return result;
        }
    }
}
