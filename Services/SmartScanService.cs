using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CCSuitePro.Helpers;
using CCSuitePro.Models;

namespace CCSuitePro.Services
{
    /// <summary>
    /// Sistema de re-scan inteligente que solo analiza archivos modificados
    /// </summary>
    public class SmartScanService
    {
        private readonly ModScannerService _scanner = new();
        private readonly ClassificationEngine _classifier = new();
        
        // Cache de última modificación por archivo
        private Dictionary<string, (DateTime LastModified, long Size, string Category)> _cache = new();
        
        public event Action<string>? OnStatusChanged;
        public event Action<int, int>? OnProgressChanged;
        
        /// <summary>
        /// Escaneo rápido: solo archivos nuevos o modificados
        /// </summary>
        public async Task<List<ModFile>> QuickScanAsync(string folderPath, List<ModFile> existingFiles)
        {
            OnStatusChanged?.Invoke("Quick scanning for changes...");
            
            var allFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".package") || f.EndsWith(".ts4script"))
                .ToList();
            
            var existing = existingFiles.ToDictionary(f => f.FilePath);
            var newFiles = new List<string>();
            var modifiedFiles = new List<string>();
            var unchangedCount = 0;
            
            foreach (var file in allFiles)
            {
                if (existing.ContainsKey(file))
                {
                    var fileInfo = new FileInfo(file);
                    var existingFile = existing[file];
                    
                    // ¿Cambió el tamaño o fecha?
                    if (fileInfo.Length != existingFile.FileSize || 
                        fileInfo.LastWriteTime > existingFile.LastModified)
                    {
                        modifiedFiles.Add(file);
                    }
                    else
                    {
                        unchangedCount++;
                    }
                }
                else
                {
                    newFiles.Add(file);
                }
            }
            
            OnStatusChanged?.Invoke($"Found {newFiles.Count} new, {modifiedFiles.Count} modified, {unchangedCount} unchanged");
            
            var filesToScan = new List<string>();
            filesToScan.AddRange(newFiles);
            filesToScan.AddRange(modifiedFiles);
            
            if (filesToScan.Count == 0)
            {
                OnStatusChanged?.Invoke("No changes detected. Everything is up to date.");
                return existingFiles;
            }
            
            // Escanear solo los que cambiaron
            var scannedFiles = new List<ModFile>();
            for (int i = 0; i < filesToScan.Count; i++)
            {
                var file = filesToScan[i];
                try
                {
                    var fileInfo = new FileInfo(file);
                    var classification = _classifier.Classify(file);
                    
                    var modFile = new ModFile
                    {
                        FilePath = file,
                        FileSize = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTime,
                        Category = classification.PrimaryCategory,
                        MD5Hash = await Helpers.MD5Helper.CalculateMD5Async(file)
                    };
                    
                    scannedFiles.Add(modFile);
                }
                catch { }
                
                OnProgressChanged?.Invoke(i + 1, filesToScan.Count);
            }
            
            // Combinar: archivos no modificados + nuevos escaneados
            var result = new List<ModFile>();
            foreach (var file in existingFiles)
            {
                if (!filesToScan.Contains(file.FilePath))
                    result.Add(file);
            }
            result.AddRange(scannedFiles);
            
            OnStatusChanged?.Invoke($"Quick scan complete. Total: {result.Count} files.");
            return result;
        }
        
        /// <summary>
        /// Verifica si un archivo está mal clasificado
        /// </summary>
        public bool IsMisclassified(ModFile file)
        {
            var newClassification = _classifier.Classify(file.FilePath);
            return newClassification.PrimaryCategory != file.Category && newClassification.Confidence > 80;
        }
        
        /// <summary>
        /// Encuentra archivos mal clasificados en la biblioteca
        /// </summary>
        public List<ModFile> FindMisclassified(List<ModFile> files)
        {
            OnStatusChanged?.Invoke("Checking for misclassified files...");
            var misclassified = new List<ModFile>();
            
            for (int i = 0; i < files.Count; i++)
            {
                if (IsMisclassified(files[i]))
                    misclassified.Add(files[i]);
                    
                if (i % 100 == 0)
                    OnProgressChanged?.Invoke(i + 1, files.Count);
            }
            
            OnStatusChanged?.Invoke($"Found {misclassified.Count} potentially misclassified files.");
            return misclassified;
        }
    }
}
