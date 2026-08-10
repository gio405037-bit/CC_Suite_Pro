using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CCSuitePro.Helpers;
using CCSuitePro.Models;

namespace CCSuitePro.Services
{
    public class ModScannerService
    {
        public event Action<string>? OnFileScanned;
        public event Action<int, int>? OnProgressChanged;
        public event Action<string>? OnScanCompleted;
        public event Action<string>? OnError;
        
        public async Task<List<ModFile>> ScanDirectoryAsync(string path)
        {
            var modFiles = new List<ModFile>();
            
            if (!Directory.Exists(path))
            {
                OnError?.Invoke($"Directory not found: {path}");
                return modFiles;
            }
            
            var extensions = new[] { ".package", ".ts4script" };
            var files = new List<string>();
            
            foreach (var ext in extensions)
            {
                try
                {
                    var found = Directory.GetFiles(path, "*" + ext, SearchOption.AllDirectories);
                    files.AddRange(found);
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Error scanning {ext}: {ex.Message}");
                }
            }
            
            // Limitar a 10000 archivos mÃ¡ximo para evitar crasheos
            // Limited to 10000 files max
            
            int total = files.Count;
            int current = 0;
            
            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    var modFile = new ModFile
                    {
                        FilePath = file,
                        FileSize = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTime,
                        Category = new ClassificationEngine().Classify(file).PrimaryCategory
                    };
                    
                    // Solo calcular MD5 para archivos grandes si es package/script
                    if (modFile.IsPackage || modFile.IsScript)
                    {
                        try
                        {
                            modFile.MD5Hash = await MD5Helper.CalculateMD5Async(file);
                        }
                        catch
                        {
                            modFile.MD5Hash = "ERROR";
                        }
                    }
                    
                    modFiles.Add(modFile);
                    current++;
                    
                    if (current % 100 == 0)
                    {
                        OnProgressChanged?.Invoke(current, total);
                    }
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Skip: {Path.GetFileName(file)} - {ex.Message}");
                }
            }
            
            OnProgressChanged?.Invoke(total, total);
            OnScanCompleted?.Invoke($"Scan complete: {modFiles.Count} files found");
            return modFiles;
        }
    }
}
