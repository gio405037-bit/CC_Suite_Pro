using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CCSuitePro.Models;

namespace CCSuitePro.Services
{
    // Stable, language-neutral category identifiers. Display labels are translated separately.
    public sealed class OrganizeMove
    {
        public string Source { get; init; } = "";
        public string Destination { get; init; } = "";
        public string CategoryId { get; init; } = "";
        public long SourceLength { get; init; } = -1;
        public DateTime SourceModifiedUtc { get; init; } = DateTime.MinValue;
    }

    public class OrganizerService
    {
        public event Action<string>? OnStatusChanged;
        public event Action<int, int>? OnProgressChanged;

        // Do NOT translate physical folder names based on the current UI language.
        // Existing release folder names are retained for compatibility.
        private static readonly Dictionary<string, string> CategoryFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Hair"] = "Hair", ["Tops"] = "Clothing - Tops",
            ["Bottoms"] = "Clothing - Bottoms", ["Full Body"] = "Clothing - Full Body",
            ["Shoes"] = "Shoes", ["Accessories"] = "Accessories",
            ["Sliders"] = "Sliders and Presets", ["Makeup & Skin"] = "Makeup and Skin",
            ["Poses & Animations"] = "Poses and Animations", ["Furniture"] = "Furniture",
            ["Build Mode"] = "Build Mode", ["Electronics"] = "Electronics",
            ["Lighting"] = "Lighting", ["Deco"] = "Deco",
            ["Scripts & Mods"] = "Scripts and Mods", ["Other"] = "Other"
        };

        public IReadOnlyList<OrganizeMove> BuildPlan(IEnumerable<ModFile> files, string basePath)
        {
            if (string.IsNullOrWhiteSpace(basePath) || !Directory.Exists(basePath))
                return Array.Empty<OrganizeMove>();
            var root = Path.GetFullPath(basePath);
            var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            var all = files.Where(f => File.Exists(f.FilePath)).ToList();
            var scriptDirs = new HashSet<string>(all.Where(f => f.IsScript)
                .Select(f => Path.GetDirectoryName(f.FilePath) ?? ""), StringComparer.OrdinalIgnoreCase);
            var scriptStems = new HashSet<string>(all.Where(f => f.IsScript)
                .Select(f => Path.GetFileNameWithoutExtension(f.FileName)), StringComparer.OrdinalIgnoreCase);
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Hair", "Tops", "Bottoms", "Full Body", "Shoes", "Accessories", "Sliders",
                "Makeup & Skin", "Furniture", "Build Mode", "Electronics", "Lighting", "Deco"
            };
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var moves = new List<OrganizeMove>();
            foreach (var file in all)
            {
                if (!file.IsPackage || file.NeedsReview || !allowed.Contains(file.Category)) continue;
                var source = Path.GetFullPath(file.FilePath);
                if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) continue;
                var folder = Path.GetDirectoryName(source) ?? root;
                if (!source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (scriptDirs.Contains(folder) || scriptStems.Contains(Path.GetFileNameWithoutExtension(file.FileName))) continue;
                var relativeDir = Path.GetRelativePath(root, folder);
                // Only root items or files already in a simple one-level category folder.
                if (relativeDir != "." && relativeDir.Contains(Path.DirectorySeparatorChar)) continue;
                if (relativeDir != "." && !CategoryFolders.Values.Contains(relativeDir, StringComparer.OrdinalIgnoreCase)) continue;
                var destination = Path.GetFullPath(Path.Combine(root, CategoryFolders[file.Category], file.FileName));
                if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    destination.Equals(source, StringComparison.OrdinalIgnoreCase) ||
                    File.Exists(destination) || !reserved.Add(destination)) continue;
                moves.Add(new OrganizeMove { Source = source, Destination = destination, CategoryId = file.Category,
                    SourceLength = new FileInfo(source).Length, SourceModifiedUtc = File.GetLastWriteTimeUtc(source) });
            }
            return moves;
        }

        // Opt-in only. Never relocate .ts4script or packages sharing a folder with scripts.
        // For ordinary CAS/BuildBuy CC, group by validated filename signature. Original category remains unchanged.
        public IReadOnlyList<OrganizeMove> BuildCreatorPlan(IEnumerable<ModFile> files, string basePath)
        {
            if (string.IsNullOrWhiteSpace(basePath) || !Directory.Exists(basePath))
                return Array.Empty<OrganizeMove>();

            var root = Path.GetFullPath(basePath);
            var rootPrefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            var list = files.Where(f => File.Exists(f.FilePath)).ToList();
            var scriptDirectories = new HashSet<string>(list.Where(f => f.IsScript)
                .Select(f => Path.GetDirectoryName(f.FilePath) ?? ""), StringComparer.OrdinalIgnoreCase);
            var safeCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Hair", "Tops", "Bottoms", "Full Body", "Shoes", "Accessories",
              "Sliders", "Makeup & Skin", "Furniture", "Build Mode", "Deco", "Electronics", "Lighting" };
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var plan = new List<OrganizeMove>();

            foreach (var file in list)
            {
                if (!file.IsPackage || file.IsScript || file.NeedsReview ||
                    !safeCategories.Contains(file.Category) ||
                    file.CreatorEvidence != "Filename" ||
                    string.IsNullOrWhiteSpace(file.CreatorName) || file.CreatorName == "Unknown") continue;

                // Do not alter anything in mod folders containing script dependencies.
                if (scriptDirectories.Contains(Path.GetDirectoryName(file.FilePath) ?? "")) continue;
                var source = Path.GetFullPath(file.FilePath);
                if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) continue;
                if (!source.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                // Never flatten a complex install tree. Only root files or one-level category folders.
                var relativeDir = Path.GetRelativePath(root, Path.GetDirectoryName(source) ?? root);
                if (relativeDir != "." && relativeDir.Contains(Path.DirectorySeparatorChar)) continue;
                var simpleFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Hair", "Tops", "Shoes", "Accessories", "Furniture", "Deco", "Build Mode",
                  "Clothing - Tops", "Clothing - Bottoms", "Clothing - Full Body",
                  "Makeup and Skin", "Sliders and Presets", "CAS", "BuildBuy" };
                if (relativeDir != "." && !simpleFolders.Contains(relativeDir)) continue;
                // Allow safe folder names based on the fixed, reviewed catalog only.
                var creatorFolder = new string(file.CreatorName.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_').ToArray()).Trim();
                if (creatorFolder.Length == 0 || creatorFolder == "." || creatorFolder == "..") continue;
                var destination = Path.GetFullPath(Path.Combine(root, "Creators", creatorFolder, file.FileName));
                if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
                    source.Equals(destination, StringComparison.OrdinalIgnoreCase) ||
                    File.Exists(destination) || !reserved.Add(destination)) continue;
                plan.Add(new OrganizeMove { Source = source, Destination = destination, CategoryId = file.Category,
                    SourceLength = new FileInfo(source).Length, SourceModifiedUtc = File.GetLastWriteTimeUtc(source) });
            }
            return plan;
        }

        public Task OrganizeFilesAsync(List<ModFile> modFiles, string basePath, bool dryRun = true)
        {
            var plan = BuildPlan(modFiles, basePath);
            if (dryRun)
            {
                OnStatusChanged?.Invoke($"Preview ready: {plan.Count} move(s). No files changed.");
                return Task.CompletedTask;
            }
            return ExecutePlanAsync(plan);
        }

        public Task ExecutePlanAsync(IReadOnlyList<OrganizeMove> plan)
        {
            return Task.Run(() =>
            {
                if (plan == null) throw new ArgumentNullException(nameof(plan));
                var moved = new List<OrganizeMove>();
                try
                {
                    // Validate ALL items before making the first change.
                    var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in plan)
                    {
                        if (!sources.Add(item.Source) || !destinations.Add(item.Destination))
                            throw new InvalidOperationException("The move plan contains duplicate paths.");
                        if (!Path.GetExtension(item.Source).Equals(".package", StringComparison.OrdinalIgnoreCase) ||
                            !File.Exists(item.Source) || File.Exists(item.Destination) ||
                            (File.GetAttributes(item.Source) & FileAttributes.ReparsePoint) != 0 ||
                            item.SourceLength < 0 ||
                            new FileInfo(item.Source).Length != item.SourceLength ||
                            File.GetLastWriteTimeUtc(item.Source) != item.SourceModifiedUtc)
                            throw new IOException($"Move plan is out of date: {Path.GetFileName(item.Source)}. Preview again.");
                    }
                    for (int i = 0; i < plan.Count; i++)
                    {
                        var item = plan[i];
                        if (!File.Exists(item.Source) || File.Exists(item.Destination) ||
                            new FileInfo(item.Source).Length != item.SourceLength ||
                            File.GetLastWriteTimeUtc(item.Source) != item.SourceModifiedUtc)
                            throw new IOException($"Source changed during organizing: {Path.GetFileName(item.Source)}.");
                        Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
                        File.Move(item.Source, item.Destination); // Mod bytes, hashes and TGI resources stay unchanged.
                        moved.Add(item);
                        OnProgressChanged?.Invoke(i + 1, plan.Count);
                    }
                    OnStatusChanged?.Invoke($"Moved {moved.Count} file(s) successfully.");
                }
                catch (Exception ex)
                {
                    var rollbackErrors = new List<string>();
                    foreach (var item in moved.AsEnumerable().Reverse())
                    {
                        try
                        {
                            if (!File.Exists(item.Destination) || File.Exists(item.Source))
                            {
                                rollbackErrors.Add(item.Destination);
                                continue;
                            }
                            Directory.CreateDirectory(Path.GetDirectoryName(item.Source)!);
                            File.Move(item.Destination, item.Source);
                        }
                        catch { rollbackErrors.Add(item.Destination); }
                    }
                    if (rollbackErrors.Count > 0)
                    {
                        var message = "Organizing failed and automatic rollback was incomplete. " +
                            "Restore from the verified backup. Files needing attention: " + string.Join(", ", rollbackErrors);
                        OnStatusChanged?.Invoke(message);
                        throw new IOException(message, ex);
                    }
                    OnStatusChanged?.Invoke("Organizing failed. Completed moves were rolled back; the backup was kept.");
                    throw new IOException("Organizing failed; completed moves were restored. " + ex.Message, ex);
                }
            });
        }
    }
}
