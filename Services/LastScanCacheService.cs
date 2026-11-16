using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CCSuitePro.Models;
using Newtonsoft.Json;

namespace CCSuitePro.Services
{
    /// <summary>
    /// One local, per-Windows-user snapshot. Never copies game content and never goes into the ZIP.
    /// Restored results are for display only until a fresh scan confirms them.
    /// </summary>
    public static class LastScanCacheService
    {
        private const int FormatVersion = 1;
        private const int MaxFiles = 150000;
        private const long MaxBytes = 100L * 1024L * 1024L;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static string CachePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CCSuitePro", "ScanCache", "last_scan.json");

        public sealed class Snapshot
        {
            public int FormatVersion { get; set; } = 1;
            public string FolderPath { get; set; } = string.Empty;
            public DateTimeOffset ScannedAtUtc { get; set; }
            public string AIStatus { get; set; } = string.Empty;
            public List<ModFile> Files { get; set; } = new();
            public List<DuplicateGroup> DuplicateGroups { get; set; } = new();
            public List<ConflictResult> Conflicts { get; set; } = new();
        }

        // Tests may pass their own temporary destination; production always calls CachePath.
        public static bool TrySave(string folder, List<ModFile> files,
            List<DuplicateGroup> duplicateGroups, List<ConflictResult> conflicts,
            string aiStatus, out string error) =>
            TrySaveToPath(CachePath, folder, files, duplicateGroups, conflicts, aiStatus, out error);

        public static bool TrySaveToPath(string cacheFile, string folder, List<ModFile> files,
            List<DuplicateGroup> duplicateGroups, List<ConflictResult> conflicts,
            string aiStatus, out string error)
        {
            error = string.Empty;
            string? temp = null;
            try
            {
                if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Scan folder is missing.");
                if (files == null || duplicateGroups == null || conflicts == null || files.Count > MaxFiles)
                    throw new InvalidDataException("Invalid number of scan entries.");
                string normalizedRoot = Path.GetFullPath(folder);
                var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var mod in files)
                {
                    if (mod == null || !ValidMod(mod, normalizedRoot) || !entries.Add(Path.GetFullPath(mod.FilePath)))
                        throw new InvalidDataException("Invalid or repeated scan file path.");
                }
                foreach (var group in duplicateGroups)
                {
                    if (group == null || group.Files == null || group.Files.Count < 2 ||
                        group.Files.Any(f => f == null || !entries.Contains(Path.GetFullPath(f.FilePath))))
                        throw new InvalidDataException("Invalid duplicate group.");
                }
                foreach (var conflict in conflicts)
                {
                    if (conflict == null || conflict.File1 == null || conflict.File2 == null ||
                        !entries.Contains(Path.GetFullPath(conflict.File1.FilePath)) ||
                        !entries.Contains(Path.GetFullPath(conflict.File2.FilePath)))
                        throw new InvalidDataException("Invalid conflict reference.");
                }

                var snapshot = new Snapshot
                {
                    FormatVersion = FormatVersion,
                    FolderPath = normalizedRoot,
                    ScannedAtUtc = DateTimeOffset.UtcNow,
                    AIStatus = aiStatus ?? string.Empty,
                    Files = files,
                    DuplicateGroups = duplicateGroups,
                    Conflicts = conflicts
                };
                string json = JsonConvert.SerializeObject(snapshot, Formatting.None);
                if (Utf8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("Scan cache exceeds size limit.");
                string? parent = Path.GetDirectoryName(Path.GetFullPath(cacheFile));
                if (string.IsNullOrWhiteSpace(parent)) throw new InvalidDataException("Invalid cache path.");
                Directory.CreateDirectory(parent);
                temp = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, json, Utf8);
                // Atomic on same volume: replace the previous snapshot, never append history.
                File.Move(temp, cacheFile, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                ex is ArgumentException || ex is JsonException || ex is InvalidDataException ||
                ex is NotSupportedException)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (temp != null)
                {
                    try { if (File.Exists(temp)) File.Delete(temp); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        public static bool TryLoad(string folder, out Snapshot snapshot) =>
            TryLoadFromPath(CachePath, folder, out snapshot);

        public static bool TryLoadFromPath(string cacheFile, string folder, out Snapshot snapshot)
        {
            snapshot = new Snapshot();
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) || !File.Exists(cacheFile))
                    return false;
                var info = new FileInfo(cacheFile);
                if (info.Length <= 0 || info.Length > MaxBytes) return false;
                var found = JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(cacheFile, Encoding.UTF8));
                if (found == null || found.FormatVersion != FormatVersion ||
                    found.Files == null || found.DuplicateGroups == null || found.Conflicts == null ||
                    found.Files.Count > MaxFiles || !SameFolder(found.FolderPath, folder)) return false;
                string normalizedRoot = Path.GetFullPath(folder);
                var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var mod in found.Files)
                {
                    if (mod == null || !ValidMod(mod, normalizedRoot) || !entries.Add(Path.GetFullPath(mod.FilePath)))
                        return false;
                }
                foreach (var group in found.DuplicateGroups)
                {
                    if (group == null || group.Files == null || group.Files.Count < 2 ||
                        group.Files.Any(f => f == null || string.IsNullOrWhiteSpace(f.FilePath) ||
                            !entries.Contains(Path.GetFullPath(f.FilePath)))) return false;
                }
                foreach (var conflict in found.Conflicts)
                {
                    if (conflict == null || conflict.File1 == null || conflict.File2 == null ||
                        string.IsNullOrWhiteSpace(conflict.File1.FilePath) ||
                        string.IsNullOrWhiteSpace(conflict.File2.FilePath) ||
                        !entries.Contains(Path.GetFullPath(conflict.File1.FilePath)) ||
                        !entries.Contains(Path.GetFullPath(conflict.File2.FilePath))) return false;
                }
                snapshot = found;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                ex is ArgumentException || ex is JsonException || ex is InvalidDataException ||
                ex is NotSupportedException)
            { return false; }
        }

        public static bool TryClear(out string error) => TryClearFromPath(CachePath, out error);

        public static bool TryClearFromPath(string cacheFile, out string error)
        {
            error = string.Empty;
            try { if (File.Exists(cacheFile)) File.Delete(cacheFile); return true; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                ex is ArgumentException || ex is NotSupportedException)
            { error = ex.Message; return false; }
        }

        private static bool SameFolder(string left, string right) =>
            string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);

        private static bool ValidMod(ModFile mod, string root)
        {
            if (string.IsNullOrWhiteSpace(mod.FilePath) || mod.FileSize < 0 || mod.FilePath.Length > 32760)
                return false;
            string full = Path.GetFullPath(mod.FilePath);
            string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            var ext = Path.GetExtension(full);
            return ext.Equals(".package", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".ts4script", StringComparison.OrdinalIgnoreCase);
        }
    }
}
