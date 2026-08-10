using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CCSuitePro.Services
{
    /// <summary>
    /// Analiza archivos por tamaño y detecta anomalías basado en su categoría
    /// </summary>
    public class SizeAnalyzer
    {
        // Rangos de tamaño por tipo (en bytes)
        private static readonly Dictionary<string, (long Tiny, long Small, long Normal, long Large)> SizeProfiles = new()
        {
            // CAS
            ["Hair"] = (500_000, 2_000_000, 15_000_000, 40_000_000),
            ["Tops"] = (300_000, 1_000_000, 10_000_000, 30_000_000),
            ["Bottoms"] = (300_000, 1_000_000, 10_000_000, 25_000_000),
            ["Full Body"] = (500_000, 2_000_000, 15_000_000, 40_000_000),
            ["Shoes"] = (200_000, 1_000_000, 8_000_000, 20_000_000),
            ["Accessories"] = (100_000, 500_000, 5_000_000, 15_000_000),
            ["Makeup & Skin"] = (100_000, 500_000, 5_000_000, 15_000_000),
            ["Sliders"] = (50_000, 200_000, 2_000_000, 10_000_000),
            
            // Build/Buy
            ["Furniture"] = (500_000, 2_000_000, 15_000_000, 50_000_000),
            ["Build Mode"] = (200_000, 1_000_000, 10_000_000, 30_000_000),
            ["Deco"] = (100_000, 500_000, 5_000_000, 20_000_000),
            
            // Animations
            ["Poses & Animations"] = (100_000, 500_000, 5_000_000, 15_000_000),
            
            // Scripts/Mods
            ["Scripts & Mods"] = (10_000, 100_000, 2_000_000, 10_000_000),
        };

        public class SizeAnalysis
        {
            public string Category { get; set; } = "";
            public long FileSize { get; set; }
            public string SizeLevel { get; set; } = "";  // Tiny, Small, Normal, Large, Extreme
            public string HealthStatus { get; set; } = "Normal";  // Normal, Warning, Suspicious
            public string Message { get; set; } = "";
            public int ResourceCount { get; set; }
            public bool IsLikelyCC { get; set; }
            public bool IsLikelyMod { get; set; }
        }

        /// <summary>
        /// Analiza un archivo por tamaño y contenido
        /// </summary>
        public SizeAnalysis Analyze(string filePath, string category, int resourceCount = 0)
        {
            var fileSize = new FileInfo(filePath).Length;
            var extension = Path.GetExtension(filePath)?.ToLower() ?? "";
            
            var analysis = new SizeAnalysis
            {
                Category = category,
                FileSize = fileSize,
                ResourceCount = resourceCount
            };

            // Si no tenemos perfil para esta categoría, usar rangos genéricos
            var profile = SizeProfiles.GetValueOrDefault(category, (100_000, 1_000_000, 10_000_000, 50_000_000));
            
            // Determinar nivel de tamaño
            if (fileSize <= profile.Tiny)
                analysis.SizeLevel = "Tiny";
            else if (fileSize <= profile.Small)
                analysis.SizeLevel = "Small";
            else if (fileSize <= profile.Normal)
                analysis.SizeLevel = "Normal";
            else if (fileSize <= profile.Large)
                analysis.SizeLevel = "Large";
            else
                analysis.SizeLevel = "Extreme";

            // Detectar si es probablemente CC o Mod
            analysis.IsLikelyCC = category != "Scripts & Mods" && fileSize > 500_000;
            analysis.IsLikelyMod = extension == ".ts4script" || (category == "Scripts & Mods" && fileSize < 10_000_000);

            // Health check basado en anomalías
            CheckAnomalies(analysis, extension, fileSize);

            return analysis;
        }

        private void CheckAnomalies(SizeAnalysis analysis, string extension, long fileSize)
        {
            // Script muy grande
            if ((extension == ".ts4script" || analysis.Category == "Scripts & Mods") && fileSize > 10_000_000)
            {
                analysis.HealthStatus = "Suspicious";
                analysis.Message = "Unusually large for a script/mod. Possible corruption or incorrect packaging.";
                return;
            }

            // CC extremadamente pequeño
            if (analysis.Category != "Scripts & Mods" && analysis.Category != "Other" && fileSize < 10_000)
            {
                analysis.HealthStatus = "Warning";
                analysis.Message = "Very small file for CC. May be incomplete or placeholder.";
                return;
            }

            // CC extremadamente grande
            if (fileSize > 100_000_000)
            {
                analysis.HealthStatus = "Warning";
                analysis.Message = $"Very large file ({fileSize / 1_000_000}MB). May contain 4K textures or unoptimized content.";
                return;
            }

            // Package muy grande sin recursos (posiblemente corrupto)
            if (extension == ".package" && fileSize > 1_000_000 && analysis.ResourceCount == 0)
            {
                analysis.HealthStatus = "Suspicious";
                analysis.Message = "Large file with no readable resources. May be corrupted.";
                return;
            }

            analysis.HealthStatus = "Normal";
        }

        /// <summary>
        /// Estima la categoría basada SOLO en el tamaño (como respaldo)
        /// </summary>
        public string GuessCategoryBySize(string filePath)
        {
            var fileSize = new FileInfo(filePath).Length;
            var extension = Path.GetExtension(filePath)?.ToLower() ?? "";

            if (extension == ".ts4script") return "Scripts & Mods";
            
            if (fileSize < 50_000) return "Scripts & Mods";  // Muy pequeño = probablemente script/tuning
            if (fileSize < 500_000) return "Makeup & Skin";   // Pequeño = maquillaje/acc
            if (fileSize < 2_000_000) return "Accessories";    // Mediano-pequeño
            if (fileSize < 8_000_000) return "Shoes";          // Mediano
            if (fileSize < 15_000_000) return "Tops";          // Normal CC
            if (fileSize < 30_000_000) return "Full Body";     // Grande
            if (fileSize < 50_000_000) return "Hair";          // Muy grande
            return "Furniture";                                 // Enorme
        }
    }
}
