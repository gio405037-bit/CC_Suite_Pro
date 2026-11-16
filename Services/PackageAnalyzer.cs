using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CCSuitePro.Services
{
    public class PackageAnalyzer
    {
        private static readonly Dictionary<uint, (string Category, string SubCategory)> ResourceTypes = new()
        {
            { 0x034AEECB, ("CAS", "CAS Part") },
            { 0x0355A0D3, ("CAS", "CAS Part Variant") },
            { 0x545AC67A, ("CAS", "Sim Data") },
            { 0x015A1849, ("Mesh", "GEOM") },
            { 0x00B2D882, ("Mesh", "Object Definition") },
            { 0x01661233, ("Mesh", "RIG") },
            { 0x00B2D7C8, ("Texture", "Image") },
            { 0x03B33DDF, ("Gameplay", "Tuning") },
            { 0x062E30D6, ("Gameplay", "Interaction") },
            { 0x0E4A20E9, ("Gameplay", "Buff") },
            { 0x0E4A20EA, ("Gameplay", "Trait") },
            { 0xFB00791E, ("Animation", "Clip") },
            { 0xFB00791F, ("Animation", "Clip Header") },
            { 0x00B2D7CA, ("Animation", "Animation") },
            { 0x0166038C, ("Strings", "String Table") },
            { 0x00B2D7CD, ("Script", "Script") },
        };

        public class ResourceProfile
        {
            public string FileName { get; set; } = "";
            public long FileSize { get; set; }
            public Dictionary<string, int> CategoryCounts { get; set; } = new();
            public int TotalResources { get; set; }
            public int CASResources { get; set; }
            public int MeshResources { get; set; }
            public int TextureResources { get; set; }
            public int GameplayResources { get; set; }
            public int AnimationResources { get; set; }
            public int ScriptResources { get; set; }
            public bool IsCAS => CASResources > 0;
            public bool IsBuildBuy => MeshResources > 3 && CASResources == 0;
            public bool IsGameplay => GameplayResources > 0;
            public bool IsAnimation => AnimationResources > 0;
            public bool IsScript => ScriptResources > 0;
        }

        public ResourceProfile Analyze(string filePath)
        {
            var profile = new ResourceProfile { FileName = Path.GetFileName(filePath ?? string.Empty) };
            if (string.IsNullOrWhiteSpace(filePath)) return profile;

            try
            {
                // A file may disappear between scanning and classification. Treat that as
                // unavailable evidence, not as a fatal classification error.
                var info = new FileInfo(filePath);
                if (!info.Exists) return profile;
                profile.FileSize = info.Length;

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fs))
                {
                    if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "DBPF") return profile;
                    fs.Seek(32, SeekOrigin.Begin);
                    var count = Math.Min(reader.ReadUInt32(), 50000);
                    fs.Seek(64, SeekOrigin.Begin);
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            var typeId = reader.ReadUInt32();
                            reader.ReadUInt32(); reader.ReadUInt64(); reader.ReadUInt32(); reader.ReadUInt32();
                            if (ResourceTypes.ContainsKey(typeId))
                            {
                                var cat = ResourceTypes[typeId].Category;
                                profile.CategoryCounts[cat] = profile.CategoryCounts.GetValueOrDefault(cat) + 1;
                                profile.TotalResources++;
                                switch (cat) {
                                    case "CAS": profile.CASResources++; break;
                                    case "Mesh": profile.MeshResources++; break;
                                    case "Texture": profile.TextureResources++; break;
                                    case "Gameplay": profile.GameplayResources++; break;
                                    case "Animation": profile.AnimationResources++; break;
                                    case "Script": profile.ScriptResources++; break;
                                }
                            }
                        }
                        catch { break; }
                    }
                }
            }
            catch { }
            return profile;
        }
    }
}