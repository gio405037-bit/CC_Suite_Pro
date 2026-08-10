using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CCSuitePro.Services
{
    public class ClassificationResult
    {
        public string PrimaryCategory { get; set; } = "Other";
        public Dictionary<string, int> Scores { get; set; } = new();
        public int Confidence { get; set; }
        public bool NeedsReview { get; set; }
        public List<string> Evidence { get; set; } = new();
        public List<string> Tags { get; set; } = new();
    }

    public class ClassificationEngine
    {
        private readonly PackageAnalyzer _analyzer = new();

        public ClassificationResult Classify(string filePath)
        {
            var result = new ClassificationResult();
            var scores = new Dictionary<string, int> { ["Hair"]=0, ["Tops"]=0, ["Bottoms"]=0, ["Full Body"]=0, ["Shoes"]=0, ["Accessories"]=0, ["Sliders"]=0, ["Makeup & Skin"]=0, ["Poses & Animations"]=0, ["Furniture"]=0, ["Build Mode"]=0, ["Deco"]=0, ["Scripts & Mods"]=0, ["Other"]=0 };
            var fn = Path.GetFileNameWithoutExtension(filePath).ToLower();
            var ext = Path.GetExtension(filePath)?.ToLower() ?? "";
            var dir = Path.GetDirectoryName(filePath)?.ToLower() ?? "";
            var size = new FileInfo(filePath).Length;

            // === EXTENSION ===
            if (ext == ".ts4script") { scores["Scripts & Mods"] += 100; result.Evidence.Add("ts4script"); }

            // === RECURSOS INTERNOS ===
            if (ext == ".package")
            {
                var p = _analyzer.Analyze(filePath);
                
                // ANIMACIONES - MÁXIMA PRIORIDAD
                if (p.IsAnimation) { scores["Poses & Animations"] += 80; result.Evidence.Add($"Anim clips: {p.AnimationResources}"); result.Tags.Add("Animation"); }
                
                if (p.IsCAS) { scores["Hair"]+=15; scores["Tops"]+=15; scores["Bottoms"]+=15; scores["Full Body"]+=15; scores["Shoes"]+=15; scores["Accessories"]+=15; scores["Makeup & Skin"]+=15; result.Evidence.Add("CAS part"); result.Tags.Add("CAS"); }
                if (p.IsGameplay) { scores["Scripts & Mods"] += 30; result.Evidence.Add($"Tuning: {p.GameplayResources}"); result.Tags.Add("Gameplay"); }
                if (p.IsBuildBuy) { scores["Furniture"]+=15; scores["Build Mode"]+=15; scores["Deco"]+=15; result.Evidence.Add("BuildBuy"); }
                if (p.IsScript) { scores["Scripts & Mods"] += 20; result.Tags.Add("Script"); }
            }

            // === NOMBRE ===
            if (fn.Contains("hair")) scores["Hair"] += 10;
            if (fn.Contains("top")||fn.Contains("shirt")) scores["Tops"] += 10;
            if (fn.Contains("pant")||fn.Contains("jean")||fn.Contains("skirt")) scores["Bottoms"] += 10;
            if (fn.Contains("dress")||fn.Contains("outfit")) scores["Full Body"] += 10;
            if (fn.Contains("shoe")||fn.Contains("heel")||fn.Contains("boot")) scores["Shoes"] += 10;
            if (fn.Contains("necklace")||fn.Contains("earring")) scores["Accessories"] += 10;
            if (fn.Contains("slider")||fn.Contains("preset")) scores["Sliders"] += 15;
            if (fn.Contains("makeup")||fn.Contains("lipstick")||fn.Contains("skin")) scores["Makeup & Skin"] += 10;
            if (fn.Contains("pose")||fn.Contains("anim")||fn.Contains("clip")||fn.Contains("dance")) { scores["Poses & Animations"] += 25; result.Evidence.Add("Name: pose/anim"); }
            if (fn.Contains("script")||fn.Contains("mod")||fn.Contains("gameplay")) scores["Scripts & Mods"] += 10;

            // === CARPETA ===
            if (dir.Contains("\\hair")) scores["Hair"] += 15;
            if (dir.Contains("\\top")) scores["Tops"] += 15;
            if (dir.Contains("\\bottom")) scores["Bottoms"] += 15;
            if (dir.Contains("\\dress")) scores["Full Body"] += 15;
            if (dir.Contains("\\shoe")) scores["Shoes"] += 15;
            if (dir.Contains("\\accessor")) scores["Accessories"] += 15;
            if (dir.Contains("\\slider")||dir.Contains("\\preset")) scores["Sliders"] += 15;
            if (dir.Contains("\\makeup")||dir.Contains("\\skin")) scores["Makeup & Skin"] += 15;
            if (dir.Contains("\\pose")||dir.Contains("\\anim")||dir.Contains("\\clip")) { scores["Poses & Animations"] += 20; result.Evidence.Add("Folder: animation"); }
            if (dir.Contains("\\script")) scores["Scripts & Mods"] += 15;

            // === TAMAÑO ===
            if (size < 100_000 && ext != ".ts4script") scores["Poses & Animations"] += 5;
            if (size > 5_000_000) { scores["Hair"]+=5; scores["Full Body"]+=5; }

            // === RESULTADO ===
            result.Scores = scores.Where(s => s.Value > 0).OrderByDescending(s => s.Value).ToDictionary(s => s.Key, s => s.Value);
            if (result.Scores.Count > 0)
            {
                var best = result.Scores.First();
                result.PrimaryCategory = best.Key;
                var total = result.Scores.Values.Sum();
                result.Confidence = (int)((double)best.Value / total * 100);
                var second = result.Scores.Count > 1 ? result.Scores.ElementAt(1).Value : 0;
                result.NeedsReview = result.Confidence < 40 || (second > 0 && (double)second / best.Value > 0.6);
            }
            else { result.PrimaryCategory = "Other"; result.Confidence = 0; result.NeedsReview = true; }
            return result;
        }
    }
}
