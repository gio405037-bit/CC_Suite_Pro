using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CCSuitePro.Services
{
    public class CleanerService
    {
        public event Action<string>? OnStatusChanged;
        public event Action<int, int>? OnProgressChanged;
        public event Action<string, int>? OnItemCleaned;
        
        public class CleanResult
        {
            public string Category { get; set; } = "";
            public int FilesFound { get; set; }
            public int FilesDeleted { get; set; }
            public long SpaceFreed { get; set; }
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
        
        public List<CleanResult> ScanForJunk(string modsFolder)
        {
            var results = new List<CleanResult>();
            
            if (!Directory.Exists(modsFolder))
            {
                OnStatusChanged?.Invoke("Folder not found!");
                return results;
            }
            
            // 1. Buscar archivos de caché
            OnStatusChanged?.Invoke("Scanning for cache files...");
            var cacheResult = ScanCacheFiles(modsFolder);
            if (cacheResult.FilesFound > 0) results.Add(cacheResult);
            
            // 2. Buscar archivos vacíos
            OnStatusChanged?.Invoke("Scanning for empty files...");
            var emptyResult = ScanEmptyFiles(modsFolder);
            if (emptyResult.FilesFound > 0) results.Add(emptyResult);
            
            // 3. Buscar archivos .tmp
            OnStatusChanged?.Invoke("Scanning for temporary files...");
            var tmpResult = ScanTempFiles(modsFolder);
            if (tmpResult.FilesFound > 0) results.Add(tmpResult);
            
            // 4. Buscar archivos corruptos
            OnStatusChanged?.Invoke("Scanning for corrupted files...");
            var corruptResult = ScanCorruptFiles(modsFolder);
            if (corruptResult.FilesFound > 0) results.Add(corruptResult);
            
            return results;
        }
        
        private CleanResult ScanCacheFiles(string folder)
        {
            var result = new CleanResult { Category = "Cache Files" };
            
            var cacheExtensions = new[] { ".cache", ".cached", ".cacheitem" };
            var cacheFolders = new[] { "cache", "caches", "thumbnails" };
            
            var files = new List<string>();
            
            // Buscar por extensión
            foreach (var ext in cacheExtensions)
            {
                try { files.AddRange(Directory.GetFiles(folder, "*" + ext, SearchOption.AllDirectories)); } catch { }
            }
            
            // Buscar en carpetas de caché
            foreach (var cacheFolder in cacheFolders)
            {
                var path = Path.Combine(folder, cacheFolder);
                if (Directory.Exists(path))
                {
                    try { files.AddRange(Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)); } catch { }
                }
            }
            
            result.FilesFound = files.Distinct().Count();
            result.FilesDeleted = 0;
            result.SpaceFreed = files.Distinct().Sum(f => { try { return new FileInfo(f).Length; } catch { return 0; } });
            
            return result;
        }
        
        private CleanResult ScanEmptyFiles(string folder)
        {
            var result = new CleanResult { Category = "Empty Files" };
            
            try
            {
                var emptyFiles = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                    .Where(f => { try { return new FileInfo(f).Length == 0; } catch { return false; } })
                    .ToList();
                
                result.FilesFound = emptyFiles.Count;
                result.FilesDeleted = 0;
                result.SpaceFreed = 0;
            }
            catch { }
            
            return result;
        }
        
        private CleanResult ScanTempFiles(string folder)
        {
            var result = new CleanResult { Category = "Temporary Files" };
            
            try
            {
                var tmpFiles = Directory.GetFiles(folder, "*.tmp", SearchOption.AllDirectories).ToList();
                result.FilesFound = tmpFiles.Count;
                result.FilesDeleted = 0;
                result.SpaceFreed = tmpFiles.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0; } });
            }
            catch { }
            
            return result;
        }
        
        private CleanResult ScanCorruptFiles(string folder)
        {
            var result = new CleanResult { Category = "Corrupted Files" };
            
            try
            {
                // Archivos .package de 0 bytes o que no se pueden leer
                var packageFiles = Directory.GetFiles(folder, "*.package", SearchOption.AllDirectories);
                var corrupt = new List<string>();
                
                foreach (var file in packageFiles)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length == 0) corrupt.Add(file);
                        else
                        {
                            // Intentar leer los primeros bytes
                            using (var fs = File.OpenRead(file))
                            {
                                var buffer = new byte[4];
                                fs.Read(buffer, 0, 4);
                            }
                        }
                    }
                    catch { corrupt.Add(file); }
                }
                
                result.FilesFound = corrupt.Count;
                result.FilesDeleted = 0;
                result.SpaceFreed = corrupt.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0; } });
            }
            catch { }
            
            return result;
        }
        
        public CleanResult CleanCategory(string modsFolder, CleanResult result)
        {
            int deleted = 0;
            
            switch (result.Category)
            {
                case "Cache Files":
                    deleted = DeleteCacheFiles(modsFolder);
                    break;
                case "Empty Files":
                    deleted = DeleteEmptyFiles(modsFolder);
                    break;
                case "Temporary Files":
                    deleted = DeleteTempFiles(modsFolder);
                    break;
                case "Corrupted Files":
                    deleted = DeleteCorruptFiles(modsFolder);
                    break;
            }
            
            result.FilesDeleted = deleted;
            OnStatusChanged?.Invoke($"Cleaned {deleted} {result.Category}");
            return result;
        }
        
        private int DeleteCacheFiles(string folder)
        {
            int count = 0;
            var cacheFolders = new[] { "cache", "caches", "thumbnails" };
            
            foreach (var cacheFolder in cacheFolders)
            {
                var path = Path.Combine(folder, cacheFolder);
                if (Directory.Exists(path))
                {
                    try
                    {
                        foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
                        {
                            try { File.Delete(file); count++; } catch { }
                        }
                    }
                    catch { }
                }
            }
            
            // También eliminar archivos .cache sueltos
            try
            {
                foreach (var file in Directory.GetFiles(folder, "*.cache", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); count++; } catch { }
                }
            }
            catch { }
            
            return count;
        }
        
        private int DeleteEmptyFiles(string folder)
        {
            int count = 0;
            try
            {
                var emptyFiles = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                    .Where(f => { try { return new FileInfo(f).Length == 0; } catch { return false; } });
                
                foreach (var file in emptyFiles)
                {
                    try { File.Delete(file); count++; } catch { }
                }
            }
            catch { }
            return count;
        }
        
        private int DeleteTempFiles(string folder)
        {
            int count = 0;
            try
            {
                foreach (var file in Directory.GetFiles(folder, "*.tmp", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); count++; } catch { }
                }
            }
            catch { }
            return count;
        }
        
        private int DeleteCorruptFiles(string folder)
        {
            int count = 0;
            try
            {
                var packageFiles = Directory.GetFiles(folder, "*.package", SearchOption.AllDirectories);
                foreach (var file in packageFiles)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length == 0) { File.Delete(file); count++; continue; }
                        
                        using (var fs = File.OpenRead(file))
                        {
                            var buffer = new byte[4];
                            if (fs.Read(buffer, 0, 4) < 4) { File.Delete(file); count++; }
                        }
                    }
                    catch { try { File.Delete(file); count++; } catch { } }
                }
            }
            catch { }
            return count;
        }
    }
}
