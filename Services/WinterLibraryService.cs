using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CCSuitePro.Models;
using Newtonsoft.Json;

namespace CCSuitePro.Services
{
    // Permanent, opt-in tools. The Time Capsule stores only statistics;
    // Guardian stores file names and MD5 values only in the user's local profile.
    public static class WinterLibraryService
    {
        private const int MaxFiles = 150000;
        private const int MaxCapsules = 120;
        private const long MaxJsonBytes = 32L * 1024 * 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static string BaseDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCSuitePro");
        public static string CapsulesPath => Path.Combine(BaseDir, "TimeCapsules", "capsules.json");
        public static string GuardianDirectory => Path.Combine(BaseDir, "ModGuardian", "baselines");

        public sealed class Capsule
        {
            public int Version { get; set; } = 1;
            public string Id { get; set; } = "";
            public DateTimeOffset CreatedUtc { get; set; }
            public DateTimeOffset ScannedUtc { get; set; }
            public int Total { get; set; }
            public int Packages { get; set; }
            public int Scripts { get; set; }
            public long Bytes { get; set; }
            public int DuplicateGroups { get; set; }
            public int PotentialConflicts { get; set; }
            public Dictionary<string, int> Categories { get; set; } = new();
        }

        public sealed class GuardianItem
        {
            // A relative filename can be personal information: never package this file.
            public string Relative { get; set; } = "";
            public long Bytes { get; set; }
            public string MD5 { get; set; } = "";
            public long ModifiedUtcTicks { get; set; }
        }
        public sealed class GuardianBaseline
        {
            public int Version { get; set; } = 1;
            public string FolderFingerprint { get; set; } = "";
            public DateTimeOffset ScannedUtc { get; set; }
            public int DuplicateGroups { get; set; }
            public List<GuardianItem> Files { get; set; } = new();
        }
        public sealed class GuardianChange
        {
            public string Kind { get; set; } = ""; // Added, Changed, Missing; never asserts deletion
            public string Relative { get; set; } = "";
        }
        public sealed class GuardianReport
        {
            public DateTimeOffset Before { get; set; }
            public DateTimeOffset After { get; set; }
            public int Added { get; set; }
            public int Changed { get; set; }
            public int Missing { get; set; }
            public int CurrentDuplicateGroups { get; set; }
            public int DuplicateGroupDelta { get; set; }
            public List<GuardianChange> Changes { get; set; } = new();
        }

        public static Capsule MakeCapsule(LastScanCacheService.Snapshot scan)
        {
            ValidateScan(scan);
            // Deliberately no FolderPath, names, hashes, creator, or source Mod objects.
            var categories = scan.Files.GroupBy(f => SafeCategory(f.Category), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).Take(32)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            return new Capsule
            {
                Id = Guid.NewGuid().ToString("N"), CreatedUtc = DateTimeOffset.UtcNow,
                ScannedUtc = scan.ScannedAtUtc, Total = scan.Files.Count,
                Packages = scan.Files.Count(f => f.IsPackage), Scripts = scan.Files.Count(f => f.IsScript),
                Bytes = scan.Files.Sum(f => f.FileSize), DuplicateGroups = scan.DuplicateGroups.Count,
                PotentialConflicts = scan.Conflicts.Count, Categories = categories
            };
        }

        private static string SafeCategory(string? raw)
        {
            // Categories are a summary, never allow filename/path-like category strings.
            if (string.IsNullOrWhiteSpace(raw) || raw.Length > 64 || raw.IndexOfAny(new[] {'/', '\\', ':', '\r', '\n'}) >= 0)
                return "Other";
            return raw.Trim();
        }

        public static List<Capsule> LoadCapsules(string? file = null)
        {
            file ??= CapsulesPath;
            if (!File.Exists(file)) return new List<Capsule>();
            var capsules = LoadJson<List<Capsule>>(file) ?? new List<Capsule>();
            if (capsules.Count > MaxCapsules || capsules.Any(c => c == null || c.Version != 1 ||
                c.Total < 0 || c.Total > MaxFiles || c.Bytes < 0 || c.Categories == null || c.Categories.Count > 32))
                throw new InvalidDataException("Invalid capsule history.");
            return capsules.OrderByDescending(c => c.CreatedUtc).ToList();
        }

        public static void SaveCapsule(Capsule capsule, string? file = null)
        {
            file ??= CapsulesPath;
            if (capsule.Version != 1 || capsule.Total < 0 || capsule.Total > MaxFiles) throw new InvalidDataException("Invalid capsule.");
            var existing = LoadCapsules(file);
            existing.Insert(0, capsule);
            WriteAtomic(file, existing.Take(MaxCapsules).ToList());
        }

        public static string GetGuardianPath(string root, string? directory = null)
        {
            string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant();
            var hash = SHA256.HashData(Utf8.GetBytes(normalized));
            return Path.Combine(directory ?? GuardianDirectory, Convert.ToHexString(hash).ToLowerInvariant() + ".json");
        }

        public static GuardianBaseline CreateBaseline(LastScanCacheService.Snapshot scan)
        {
            ValidateScan(scan);
            var root = Path.GetFullPath(scan.FolderPath);
            var items = scan.Files.Select(f => new GuardianItem
            {
                Relative = GetSafeRelative(root, f.FilePath), Bytes = f.FileSize,
                MD5 = f.MD5Hash ?? "", ModifiedUtcTicks = f.LastModified.ToUniversalTime().Ticks
            }).ToList();
            if (items.Select(i => i.Relative).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
                throw new InvalidDataException("Duplicate relative file names.");
            return new GuardianBaseline
            {
                FolderFingerprint = Path.GetFileNameWithoutExtension(GetGuardianPath(root)),
                ScannedUtc = scan.ScannedAtUtc, DuplicateGroups = scan.DuplicateGroups.Count, Files = items
            };
        }

        private static string GetSafeRelative(string root, string path)
        {
            var full = Path.GetFullPath(path);
            var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Scan contains a file outside selected Mods folder.");
            var relative = Path.GetRelativePath(root, full);
            if (string.IsNullOrEmpty(relative) || relative.StartsWith("..", StringComparison.Ordinal))
                throw new InvalidDataException("Unsafe relative name.");
            return relative.Replace('\\', '/');
        }

        public static void SaveBaseline(string root, GuardianBaseline baseline, string? directory = null)
        {
            if (baseline.Version != 1 || baseline.Files.Count > MaxFiles ||
                !string.Equals(baseline.FolderFingerprint,
                    Path.GetFileNameWithoutExtension(GetGuardianPath(root)), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Baseline does not match the selected library.");
            WriteAtomic(GetGuardianPath(root, directory), baseline);
        }

        public static GuardianBaseline? LoadBaseline(string root, string? directory = null)
        {
            var path = GetGuardianPath(root, directory);
            if (!File.Exists(path)) return null;
            var baseline = LoadJson<GuardianBaseline>(path);
            if (baseline == null || baseline.Version != 1 || baseline.Files == null || baseline.Files.Count > MaxFiles ||
                baseline.Files.Any(f => f == null || string.IsNullOrWhiteSpace(f.Relative) || f.Bytes < 0) ||
                !string.Equals(baseline.FolderFingerprint, Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid or mismatched Mod Guardian baseline.");
            return baseline;
        }

        public static GuardianReport Compare(GuardianBaseline before, GuardianBaseline after)
        {
            if (before.Version != 1 || after.Version != 1 ||
                !string.Equals(before.FolderFingerprint, after.FolderFingerprint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Cannot compare different libraries.");
            var oldFiles = before.Files.ToDictionary(f => f.Relative, StringComparer.OrdinalIgnoreCase);
            var newFiles = after.Files.ToDictionary(f => f.Relative, StringComparer.OrdinalIgnoreCase);
            var report = new GuardianReport
            {
                Before = before.ScannedUtc, After = after.ScannedUtc,
                CurrentDuplicateGroups = after.DuplicateGroups,
                DuplicateGroupDelta = after.DuplicateGroups - before.DuplicateGroups
            };
            foreach (var item in newFiles.Values)
            {
                if (!oldFiles.TryGetValue(item.Relative, out var previous))
                    report.Changes.Add(new GuardianChange { Kind = "Added", Relative = item.Relative });
                else if (item.Bytes != previous.Bytes ||
                    (!string.IsNullOrEmpty(item.MD5) && !string.IsNullOrEmpty(previous.MD5)
                        ? !string.Equals(item.MD5, previous.MD5, StringComparison.OrdinalIgnoreCase)
                        : item.ModifiedUtcTicks != previous.ModifiedUtcTicks))
                    report.Changes.Add(new GuardianChange { Kind = "Changed", Relative = item.Relative });
            }
            foreach (var item in oldFiles.Values)
                if (!newFiles.ContainsKey(item.Relative))
                    report.Changes.Add(new GuardianChange { Kind = "Missing", Relative = item.Relative });
            report.Added = report.Changes.Count(c => c.Kind == "Added");
            report.Changed = report.Changes.Count(c => c.Kind == "Changed");
            report.Missing = report.Changes.Count(c => c.Kind == "Missing");
            report.Changes = report.Changes.OrderBy(c => c.Kind).ThenBy(c => c.Relative,
                StringComparer.OrdinalIgnoreCase).ToList();
            return report;
        }

        public static void ValidateScan(LastScanCacheService.Snapshot scan)
        {
            if (scan == null || string.IsNullOrWhiteSpace(scan.FolderPath) || scan.Files == null ||
                scan.DuplicateGroups == null || scan.Conflicts == null || scan.Files.Count > MaxFiles ||
                scan.Files.Any(f => f == null || f.FileSize < 0))
                throw new InvalidDataException("Valid completed scan required.");
        }

        private static T? LoadJson<T>(string file)
        {
            if (new FileInfo(file).Length > MaxJsonBytes) throw new InvalidDataException("Local data file is too large.");
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(file, Encoding.UTF8));
        }

        private static void WriteAtomic<T>(string path, T data)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(dir)) throw new InvalidDataException("Invalid local data path.");
            Directory.CreateDirectory(dir);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                if (Utf8.GetByteCount(json) > MaxJsonBytes) throw new InvalidDataException("Local data exceeds size limit.");
                File.WriteAllText(temporary, json, Utf8);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
            }
        }
    }
}
