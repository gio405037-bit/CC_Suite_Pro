using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
