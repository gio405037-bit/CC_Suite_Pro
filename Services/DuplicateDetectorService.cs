using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCSuitePro.Models;

namespace CCSuitePro.Services
{
    public class DuplicateDetectorService
    {
        public event Action<string>? OnStatusChanged;
        
        public List<DuplicateGroup> FindDuplicates(List<ModFile> modFiles)
        {
            OnStatusChanged?.Invoke("Finding duplicates...");
            
            // Solo archivos con MD5 calculado
            var filesWithHash = modFiles
                .Where(f => !string.IsNullOrEmpty(f.MD5Hash))
                .ToList();
            
            if (filesWithHash.Count == 0)
            {
                OnStatusChanged?.Invoke("No files with MD5 hash found. Scan first.");
                return new List<DuplicateGroup>();
            }
            
            var groups = filesWithHash
                .GroupBy(f => f.MD5Hash)
                .Where(g => g.Count() > 1)
                .Select(g => new DuplicateGroup
                {
                    MD5Hash = g.Key,
                    Files = g.OrderByDescending(f => f.LastModified).ToList()
                })
                .OrderByDescending(g => g.WastedSpace)
                .ToList();
            
            OnStatusChanged?.Invoke($"Found {groups.Count} duplicate groups.");
            return groups;
        }
        
        public void DeleteDuplicates(DuplicateGroup group, bool keepNewest = true)
        {
            if (group == null || group.Files.Count <= 1) return;
            
            // Mantener el primer archivo, eliminar el resto
            var filesToDelete = keepNewest
                ? group.Files.Skip(1)
                : group.Files.Take(group.Files.Count - 1);
            
            foreach (var file in filesToDelete)
            {
                try
                {
                    if (File.Exists(file.FilePath))
                    {
                        File.Delete(file.FilePath);
                        OnStatusChanged?.Invoke($"Deleted: {file.FileName}");
                    }
                }
                catch (Exception ex)
                {
                    OnStatusChanged?.Invoke($"Error: {ex.Message}");
                }
            }
        }
    }
}
