using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using CCSuitePro.Models;

namespace CCSuitePro.Services
{
    public sealed class DuplicateDetectorService
    {
        private readonly string _quarantineRoot;

        public DuplicateDetectorService(string? quarantineRoot = null)
        {
            _quarantineRoot = quarantineRoot ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CCSuitePro", "Quarantine");
        }

        public event Action<string>? OnStatusChanged;

        public List<DuplicateGroup> FindDuplicates(List<ModFile> modFiles)
        {
            OnStatusChanged?.Invoke("Finding byte-identical duplicates...");
            var filesWithHash = (modFiles ?? new List<ModFile>())
                .Where(f => f != null && !string.IsNullOrWhiteSpace(f.MD5Hash) &&
                    f.MD5Hash.Length == 32 && f.MD5Hash.All(Uri.IsHexDigit) && File.Exists(f.FilePath))
                .ToList();
            if (filesWithHash.Count == 0)
            {
                OnStatusChanged?.Invoke("No files with MD5 hash found. Scan first.");
                return new List<DuplicateGroup>();
            }

            var groups = new List<DuplicateGroup>();
            foreach (var candidates in filesWithHash
                .GroupBy(f => (Hash: f.MD5Hash.ToUpperInvariant(), Size: f.FileSize))
                .Where(g => g.Count() > 1))
            {
                var pending = candidates.OrderByDescending(f => f.LastModified).ThenBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
                while (pending.Count > 1)
                {
                    var first = pending[0];
                    pending.RemoveAt(0);
                    var identical = pending.Where(f => FilesEqual(first.FilePath, f.FilePath)).ToList();
                    if (identical.Count == 0) continue;
                    groups.Add(new DuplicateGroup
                    {
                        MD5Hash = candidates.Key.Hash,
                        Files = new List<ModFile> { first }.Concat(identical).ToList()
                    });
                    foreach (var item in identical) pending.Remove(item);
                }
            }
            groups = groups.OrderByDescending(g => g.WastedSpace).ToList();
            OnStatusChanged?.Invoke($"Found {groups.Count} duplicate groups.");
            return groups;
        }

        private static bool FilesEqual(string a, string b)
        {
            try
            {
                using var left = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var right = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (left.Length != right.Length) return false;
                var l = new byte[65536];
                var r = new byte[65536];
                long remaining = left.Length;
                while (remaining > 0)
                {
                    int n = (int)Math.Min(l.Length, remaining);
                    left.ReadExactly(l.AsSpan(0, n));
                    right.ReadExactly(r.AsSpan(0, n));
                    if (!l.AsSpan(0, n).SequenceEqual(r.AsSpan(0, n))) return false;
                    remaining -= n;
                }
                return true;
            }
            catch { return false; }
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

        private static string Sha256(string path)
        {
            using var input = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(input));
        }

        /// <summary>Quarantines exactly the selected copy after confirming another byte-identical copy remains.</summary>
        public QuarantineActionResult QuarantineDuplicateFile(string modsRoot, DuplicateGroup group, ModFile target)
            => QuarantineDuplicateFileCore(modsRoot, group, target, null);

        private QuarantineActionResult QuarantineDuplicateFileCore(string modsRoot, DuplicateGroup group, ModFile target, string? sharedQuarantineFolder)
        {
            if (group == null || target == null || group.Files == null || group.Files.Count < 2)
                return new QuarantineActionResult { Message = "The duplicate group is no longer valid." };
            if (!group.Files.Any(f => string.Equals(f.FilePath, target.FilePath, StringComparison.OrdinalIgnoreCase)))
                return new QuarantineActionResult { Message = "The selected file is not part of this duplicate group." };
            if (!Directory.Exists(modsRoot) || !IsInsideRoot(modsRoot, target.FilePath) || !File.Exists(target.FilePath))
                return new QuarantineActionResult { Message = "The selected file is missing or outside the Mods folder." };

            try
            {
                if ((File.GetAttributes(target.FilePath) & FileAttributes.ReparsePoint) != 0)
                    return new QuarantineActionResult { Message = "Symbolic links and reparse points are not moved." };

                var survivor = group.Files.FirstOrDefault(other =>
                    !string.Equals(other.FilePath, target.FilePath, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(other.FilePath) && IsInsideRoot(modsRoot, other.FilePath) &&
                    (File.GetAttributes(other.FilePath) & FileAttributes.ReparsePoint) == 0 &&
                    FilesEqual(target.FilePath, other.FilePath));
                if (survivor == null)
                    return new QuarantineActionResult { Message = "No byte-identical copy remains. The file was left untouched." };

                var before = new FileInfo(target.FilePath);
                long size = before.Length;
                DateTime lastWriteUtc = before.LastWriteTimeUtc;
                string sha256 = Sha256(target.FilePath);
                // Guard against a stale scan or a concurrent edit immediately before the move.
                if (!FilesEqual(target.FilePath, survivor.FilePath))
                    return new QuarantineActionResult { Message = "The files changed after scanning. Refresh duplicates and try again." };

                bool ownsQuarantineFolder = string.IsNullOrWhiteSpace(sharedQuarantineFolder);
                string quarantine = sharedQuarantineFolder ?? Path.Combine(_quarantineRoot,
                    "Duplicates_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(quarantine);
                string actionId = Guid.NewGuid().ToString("N");
                string destination = Path.Combine(quarantine, actionId + "_" + Path.GetFileName(target.FilePath));
                string manifestPath = Path.Combine(quarantine, "manifest_" + actionId + ".json");
                try
                {
                    // Copy first because Mods and LocalAppData may live on different volumes.
                    // The source is removed only after the quarantine copy and manifest verify.
                    File.Copy(target.FilePath, destination, false);
                    string copiedHash = Sha256(destination);
                    if (!string.Equals(sha256, copiedHash, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("SHA-256 changed during quarantine verification.");
                    var manifest = new
                    {
                        reason = "Byte-identical duplicate",
                        originalPath = target.FilePath,
                        quarantinedPath = destination,
                        keptCopyPath = survivor.FilePath,
                        md5 = target.MD5Hash,
                        sha256,
                        size,
                        originalLastWriteUtc = lastWriteUtc,
                        movedAtUtc = DateTime.UtcNow
                    };
                    File.WriteAllText(manifestPath,
                        JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                    File.Delete(target.FilePath);
                }
                catch
                {
                    try { if (File.Exists(target.FilePath) && File.Exists(destination)) File.Delete(destination); } catch { }
                    try { if (File.Exists(manifestPath)) File.Delete(manifestPath); } catch { }
                    try { if (ownsQuarantineFolder && Directory.Exists(quarantine) && !Directory.EnumerateFileSystemEntries(quarantine).Any()) Directory.Delete(quarantine); } catch { }
                    throw;
                }

                string message = $"Quarantined {target.FileName} to: {destination}. Identical copy kept: {survivor.FileName}.";
                OnStatusChanged?.Invoke(message);
                return new QuarantineActionResult
                {
                    Success = true,
                    FilesQuarantined = 1,
                    QuarantinePath = destination,
                    Message = message
                };
            }
            catch (Exception ex)
            {
                return new QuarantineActionResult { Message = "Quarantine failed: " + ex.Message };
            }
        }

        /// <summary>Keeps the newest copy and quarantines every other copy after per-file revalidation.</summary>
        public QuarantineActionResult QuarantineDuplicateCopies(string modsRoot, DuplicateGroup group, bool keepNewest = true)
        {
            if (group == null || group.Files == null || group.Files.Count < 2)
                return new QuarantineActionResult { Message = "The duplicate group is no longer valid." };
            var existing = group.Files.Where(f => f != null && File.Exists(f.FilePath))
                .OrderByDescending(f => f.LastModified)
                .ThenBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
            if (existing.Count < 2) return new QuarantineActionResult { Message = "Fewer than two copies remain." };
            var survivor = keepNewest ? existing[0] : existing[^1];
            string quarantineFolder = Path.Combine(_quarantineRoot,
                "Duplicates_Group_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(quarantineFolder);
            int moved = 0;
            var messages = new List<string>();
            foreach (var item in existing.Where(f => !string.Equals(f.FilePath, survivor.FilePath, StringComparison.OrdinalIgnoreCase)))
            {
                var result = QuarantineDuplicateFileCore(modsRoot, group, item, quarantineFolder);
                if (result.Success) moved += result.FilesQuarantined;
                else messages.Add(result.Message);
            }
            if (moved == 0)
            {
                try { if (Directory.Exists(quarantineFolder) && !Directory.EnumerateFileSystemEntries(quarantineFolder).Any()) Directory.Delete(quarantineFolder); } catch { }
                return new QuarantineActionResult { Message = messages.FirstOrDefault() ?? "No duplicate copies were moved." };
            }
            string summary = $"Kept {survivor.FileName}; quarantined {moved} duplicate copy/copies. Restore manifests: {quarantineFolder}";
            if (messages.Count > 0) summary += " Some items were skipped: " + string.Join(" | ", messages);
            return new QuarantineActionResult { Success = true, FilesQuarantined = moved, QuarantinePath = quarantineFolder, Message = summary };
        }

        // Backward-compatible wrapper for older callers. The UI uses the root-scoped methods above.
        [Obsolete("Use QuarantineDuplicateCopies with the selected Mods root.")]
        public void DeleteDuplicates(DuplicateGroup group, bool keepNewest = true)
        {
            if (group?.Files == null || group.Files.Count < 2) return;
            string? root = Path.GetDirectoryName(group.Files[0].FilePath);
            if (string.IsNullOrWhiteSpace(root)) return;
            _ = QuarantineDuplicateCopies(root, group, keepNewest);
        }
    }
}
