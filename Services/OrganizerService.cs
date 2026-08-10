using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CCSuitePro.Helpers;
using CCSuitePro.Models;

namespace CCSuitePro.Services
{
    public class OrganizerService
    {
        public event Action<string>? OnStatusChanged;
        public event Action<int, int>? OnProgressChanged;
        
        private readonly Dictionary<string, string> _categoryFolders = new()
        {
            {"Hair", "Hair"},
            {"Tops", "Clothing - Tops"},
            {"Bottoms", "Clothing - Bottoms"},
            {"Full Body", "Clothing - Full Body"},
            {"Shoes", "Shoes"},
            {"Accessories", "Accessories"},
            {"Sliders", "Sliders and Presets"},
            {"Makeup & Skin", "Makeup and Skin"},
            {"Poses & Animations", "Poses and Animations"},
            {"Furniture", "Furniture"},
            {"Build Mode", "Build Mode"},
            {"Electronics", "Electronics"},
            {"Lighting", "Lighting"},
            {"Deco", "Deco"},
            {"Scripts & Mods", "Scripts and Mods"},
            {"Other", "Other"}
        };
        
        public Task OrganizeFilesAsync(List<ModFile> modFiles, string basePath, bool dryRun = true)
        {
            return Task.Run(() =>
            {
                OnStatusChanged?.Invoke(dryRun ? "Analyzing..." : "Organizing...");
                int moved = 0, errors = 0, updated = 0;
                
                for (int i = 0; i < modFiles.Count; i++)
                {
                    var file = modFiles[i];
                    try
                    {
                        var newCategory = FileHelper.GetCategoryFromPath(file.FilePath);
                        if (file.Category != newCategory) { file.Category = newCategory; updated++; }
                        
                        var targetFolder = _categoryFolders.GetValueOrDefault(newCategory, "Other");
                        var targetPath = Path.Combine(basePath, targetFolder);
                        var targetFile = Path.Combine(targetPath, file.FileName);
                        
                        if (file.FilePath == targetFile) continue;
                        if (File.Exists(targetFile)) continue;
                        
                        if (!dryRun)
                        {
                            if (!Directory.Exists(targetPath)) Directory.CreateDirectory(targetPath);
                            File.Move(file.FilePath, targetFile);
                            file.FilePath = targetFile;
                        }
                        moved++;
                    }
                    catch { errors++; }
                    OnProgressChanged?.Invoke(i + 1, modFiles.Count);
                }
                OnStatusChanged?.Invoke($"Done! Updated: {updated}, Moved: {moved}, Errors: {errors}");
            });
        }
    }
}
