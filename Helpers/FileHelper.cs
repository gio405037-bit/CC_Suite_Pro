using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCSuitePro.Services;

namespace CCSuitePro.Helpers
{
    public static class FileHelper
    {
        private static PackageAnalyzer? _analyzer;
        private static PackageAnalyzer Analyzer => _analyzer ??= new PackageAnalyzer();

        public static string GetCategoryFromPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return "Other";
            
            var path = filePath.ToLower();
            var fileName = Path.GetFileNameWithoutExtension(filePath).ToLower();
            var extension = Path.GetExtension(filePath)?.ToLower() ?? "";
            var directory = Path.GetDirectoryName(filePath)?.ToLower() ?? "";

            if (extension == ".ts4script") return "Scripts & Mods";

            if (directory.Contains("\\hair")) return "Hair";
            if (directory.Contains("\\top")) return "Tops";
            if (directory.Contains("\\bottom")) return "Bottoms";
            if (directory.Contains("\\dress") || directory.Contains("\\fullbody") || directory.Contains("\\outfit")) return "Full Body";
            if (directory.Contains("\\shoe") || directory.Contains("\\footwear")) return "Shoes";
            if (directory.Contains("\\accessor")) return "Accessories";
            if (directory.Contains("\\slider") || directory.Contains("\\preset")) return "Sliders";
            if (directory.Contains("\\makeup") || directory.Contains("\\skintone") || directory.Contains("\\skin")) return "Makeup & Skin";
            if (directory.Contains("\\pose") || directory.Contains("\\animation")) return "Poses & Animations";
            if (directory.Contains("\\furniture")) return "Furniture";
            if (directory.Contains("\\build")) return "Build Mode";
            if (directory.Contains("\\deco") || directory.Contains("\\clutter")) return "Deco";
            if (directory.Contains("\\script") && !directory.EndsWith("\\mods") && !directory.EndsWith("\\mods\\")) return "Scripts & Mods";

            if (fileName.Contains("slider") || fileName.Contains("preset") || fileName.Contains("body shape")) return "Sliders";
            if (fileName.Contains("pose_") || fileName.Contains("_pose") || fileName.Contains("anim_")) return "Poses & Animations";
            if (fileName.Contains("shoe") || fileName.Contains("heel") || fileName.Contains("boot") || fileName.Contains("sneaker")) return "Shoes";
            if (fileName.Contains("hair") || fileName.Contains("hairstyle") || fileName.Contains("bangs")) return "Hair";
            if (fileName.Contains("necklace") || fileName.Contains("earring") || fileName.Contains("bracelet")) return "Accessories";
            if (fileName.Contains("lipstick") || fileName.Contains("eyeshadow") || fileName.Contains("eyeliner")) return "Makeup & Skin";
            if (fileName.Contains("chair") || fileName.Contains("table") || fileName.Contains("sofa")) return "Furniture";
            if (fileName.Contains("wall") || fileName.Contains("floor") || fileName.Contains("door")) return "Build Mode";

            if (!fileName.Contains("script") && !fileName.Contains("mod_") && !fileName.Contains("_mod") && !fileName.Contains("gameplay") && !fileName.Contains("tuning"))
            {
                if (fileName.Contains("top") || fileName.Contains("shirt") || fileName.Contains("blouse")) return "Tops";
                if (fileName.Contains("pant") || fileName.Contains("jean") || fileName.Contains("skirt")) return "Bottoms";
                if (fileName.Contains("dress") || fileName.Contains("suit") || fileName.Contains("outfit")) return "Full Body";
                if (fileName.Contains("skin") || fileName.Contains("overlay")) return "Makeup & Skin";
            }

            if (fileName.Contains("script") || fileName.Contains("mod") || fileName.Contains("gameplay") || fileName.Contains("tuning"))
                return "Scripts & Mods";

            return "Other";
        }

        public static string GetCategoryWithAnalysis(string filePath)
        {
            var category = GetCategoryFromPath(filePath);
            if (category == "Other" || category == "Scripts & Mods")
            {
                try
                {
                    if (Path.GetExtension(filePath)?.ToLower() == ".package")
                    {
                        var profile = new PackageAnalyzer().Analyze(filePath);
                        
                        if (profile.IsCAS && profile.TextureResources > 3)
                            return "Makeup & Skin";
                        if (profile.IsGameplay && profile.GameplayResources > 2)
                            return "Scripts & Mods";
                        if (profile.IsAnimation && profile.AnimationResources > 0)
                            return "Poses & Animations";
                        if (profile.IsBuildBuy && profile.MeshResources > 5 && !profile.IsCAS)
                            return "Build Mode";
                        if (profile.IsScript)
                            return "Scripts & Mods";
                    }
                }
                catch { }
            }
            return category;
        }

                
        public static string GetRelativePath(string basePath, string fullPath)
        {
            if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(fullPath)) return fullPath;
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString())) basePath += Path.DirectorySeparatorChar;
            var uri = new Uri(basePath);
            var fullUri = new Uri(fullPath);
            var relativeUri = uri.MakeRelativeUri(fullUri);
            return Uri.UnescapeDataString(relativeUri.ToString().Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
