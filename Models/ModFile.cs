using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCSuitePro.Services;
using Newtonsoft.Json;

namespace CCSuitePro.Models
{
    public class ModFile
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public string Extension => Path.GetExtension(FilePath)?.ToLower() ?? string.Empty;
        public long FileSize { get; set; }
        public DateTime LastModified { get; set; }
        public string MD5Hash { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string CreatorName { get; set; } = "Unknown";
        public string CreatorEvidence { get; set; } = "None";
        public int ClassificationConfidence { get; set; }
        public bool NeedsReview { get; set; }
        // Advice is kept separate from actual organizer/category and Sims file hashes.
        public string AISuggestedCategory { get; set; } = "";
        public int AIScore { get; set; }
        public string AIScoreDisplay => AIScore > 0 ? $"{AIScore} / 100" : "n/a";
        public string AIExplanation { get; set; } = "AI not trained yet.";
        public string AIReviewState => NeedsReview ? "Review" : "Ready";

        // Presentation-only labels. These properties are NOT serialized to the last-scan cache.
        // Never translate Category, AISuggestedCategory, creator IDs or stored AIExplanation.
        [JsonIgnore] public string CategoryDisplay => LocalizedCategory(Category);
        [JsonIgnore] public string AISuggestedCategoryDisplay => LocalizedCategory(AISuggestedCategory);
        [JsonIgnore] public string CreatorDisplay => CreatorName == "Unknown"
            ? LanguageService.Instance["display_unknown"] : CreatorName;
        [JsonIgnore] public string AIReviewStateDisplay => LanguageService.Instance[
            NeedsReview ? "display_review" : "display_ready"];
        [JsonIgnore] public string AIExplanationDisplay => PythonSuggestionService.LocalizeExplanation(this);

        private static string LocalizedCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return category;
            string key = category switch
            {
                "Hair" => "display_cat_hair",
                "Tops" => "display_cat_tops",
                "Bottoms" => "display_cat_bottoms",
                "Full Body" => "display_cat_fullbody",
                "Shoes" => "display_cat_shoes",
                "Accessories" => "display_cat_accessories",
                "Sliders" => "display_cat_sliders",
                "Makeup & Skin" => "display_cat_makeup",
                "Poses & Animations" => "display_cat_poses",
                "Furniture" => "display_cat_furniture",
                "Build Mode" => "display_cat_build",
                "Electronics" => "display_cat_electronics",
                "Lighting" => "display_cat_lighting",
                "Deco" => "display_cat_deco",
                "Scripts & Mods" => "display_cat_scripts",
                "Other" => "display_cat_other",
                "Unknown" => "display_unknown",
                _ => string.Empty
            };
            return key.Length == 0 ? category : LanguageService.Instance[key];
        }

        
        public string FileSizeFormatted
        {
            get
            {
                string[] sizes = { "B", "KB", "MB", "GB" };
                double len = FileSize;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len /= 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }
        
        public string LastModifiedFormatted => LastModified.ToString("dd/MM/yyyy HH:mm");
        public bool IsPackage => Extension == ".package";
        public bool IsScript => Extension == ".ts4script";
    }

    public class DuplicateGroup
    {
        public string MD5Hash { get; set; } = string.Empty;
        public List<ModFile> Files { get; set; } = new List<ModFile>();
        
        public long WastedSpace
        {
            get
            {
                if (Files.Count > 1)
                {
                    var ordered = Files.OrderByDescending(f => f.LastModified).ToList();
                    return ordered.Skip(1).Sum(f => f.FileSize);
                }
                return 0;
            }
        }
    }

    public class ConflictResult
    {
        public ModFile? File1 { get; set; }
        public ModFile? File2 { get; set; }
        public string ConflictType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class LibraryStats
    {
        public int TotalFiles { get; set; }
        public int TotalPackages { get; set; }
        public int TotalScripts { get; set; }
        public long TotalSize { get; set; }
        public int DuplicateGroups { get; set; }
        public int DuplicateFiles { get; set; }
        public int ConflictsFound { get; set; }
        public long WastedSpace { get; set; }
        public Dictionary<string, int> CategoriesBreakdown { get; set; } = new Dictionary<string, int>();
    }

    public class BackupInfo
    {
        public string BackupPath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public long Size { get; set; }
        public int FilesCount { get; set; }
        public string Type { get; set; } = "Full";
    }

    public class AppSettings
    {
        public string ModsFolderPath { get; set; } = string.Empty;
        public string BackupFolderPath { get; set; } = string.Empty;
        public string Theme { get; set; } = "Dark";
        public string Language { get; set; } = "en";
        public bool AutoScan { get; set; } = false;
        public bool AutoBackup { get; set; } = false;
        public int BackupIntervalDays { get; set; } = 7;
        public List<string> ExcludedFolders { get; set; } = new List<string>();
        public List<string> ExcludedExtensions { get; set; } = new List<string>();
    }
}
