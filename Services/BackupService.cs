using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace CCSuitePro.Services
{
    public class BackupService
    {
        public event Action<string>? OnStatusChanged;
        public event Action<int, int>? OnProgressChanged;
        
        public Task CreateFullBackupAsync(string sourcePath, string backupPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    OnStatusChanged?.Invoke("Creating backup...");
                    
                    if (!Directory.Exists(sourcePath))
                    {
                        OnStatusChanged?.Invoke("Error: Source folder not found!");
                        return;
                    }
                    
                    if (!Directory.Exists(backupPath))
                        Directory.CreateDirectory(backupPath);
                    
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    var backupDir = Path.Combine(backupPath, $"Backup_{timestamp}");
                    Directory.CreateDirectory(backupDir);
                    
                    var files = Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories);
                    int total = files.Length;
                    int copied = 0;
                    
                    foreach (var file in files)
                    {
                        try
                        {
                            var relativePath = Path.GetRelativePath(sourcePath, file);
                            var destPath = Path.Combine(backupDir, relativePath);
                            
                            var destDir = Path.GetDirectoryName(destPath);
                            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                                Directory.CreateDirectory(destDir);
                            
                            File.Copy(file, destPath, true);
                            copied++;
                        }
                        catch
                        {
                            // Saltar archivos con error
                        }
                    }
                    
                    // Comprimir
                    var zipPath = backupDir + ".zip";
                    if (File.Exists(zipPath)) File.Delete(zipPath);
                    ZipFile.CreateFromDirectory(backupDir, zipPath);
                    
                    // Limpiar carpeta temporal
                    Directory.Delete(backupDir, true);
                    
                    var sizeMB = new FileInfo(zipPath).Length / 1024.0 / 1024.0;
                    OnStatusChanged?.Invoke($"Backup complete! {sizeMB:F1}MB - {copied}/{total} files");
                }
                catch (Exception ex)
                {
                    OnStatusChanged?.Invoke($"Backup failed: {ex.Message}");
                }
            });
        }
    }
}
