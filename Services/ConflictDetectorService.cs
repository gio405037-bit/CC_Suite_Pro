using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCSuitePro.Models;

namespace CCSuitePro.Services
{
    public class ConflictDetectorService
    {
        public event Action<string>? OnStatusChanged;
        
        public List<ConflictResult> DetectConflicts(List<ModFile> modFiles)
        {
            OnStatusChanged?.Invoke("Checking for conflicts...");
            var conflicts = new List<ConflictResult>();
            
            if (modFiles == null || modFiles.Count == 0)
            {
                OnStatusChanged?.Invoke("No files to check.");
                return conflicts;
            }
            
            var relevantFiles = modFiles
                .Where(f => f != null && (f.IsPackage || f.IsScript) && f.MD5Hash.Length == 32 && f.MD5Hash.All(Uri.IsHexDigit))
                .ToList();
            
            if (relevantFiles.Count < 2) return conflicts;
            
            var nameGroups = relevantFiles
                .GroupBy(f => Path.GetFileNameWithoutExtension(f.FileName)?.ToLower() ?? "")
                .Where(g => g.Count() > 1);
            
            foreach (var group in nameGroups)
            {
                var files = group.ToList();
                for (int i = 0; i < files.Count; i++)
                {
                    for (int j = i + 1; j < files.Count; j++)
                    {
                        var f1 = files[i];
                        var f2 = files[j];
                        
                        if (f1 == null || f2 == null) continue;
                        if (f1.Extension != f2.Extension) continue; // package+script pairs are commonly companions, NOT conflicts.
                        if (f1.MD5Hash == f2.MD5Hash) continue;
                        
                        conflicts.Add(new ConflictResult
                        {
                            File1 = f1,
                            File2 = f2,
                            ConflictType = "Possible Version Overlap",
                            Description = $"Same base name but different contents: {f1.FileName} / {f2.FileName}. Manual review required; not a confirmed conflict."
                        });
                    }
                }
            }
            
            var result = conflicts; // Do not hide results behind an arbitrary limit.
            OnStatusChanged?.Invoke($"Found {result.Count} conflicts");
            return result;
        }
    }
}
