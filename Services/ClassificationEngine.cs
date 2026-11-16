using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CCSuitePro.Services
{
    public sealed class ClassificationResult
    {
        public string PrimaryCategory { get; set; } = "Other";
        public Dictionary<string, int> Scores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public int Confidence { get; set; }
        public bool NeedsReview { get; set; } = true;
        public List<string> Evidence { get; set; } = new();
        public List<string> Tags { get; set; } = new();
    }

    /// <summary>
    /// Conservative Sims 4 CC classifier. Strong filename/folder evidence wins;
    /// generic CAS resources never imply Hair, and file size is never used as a category.
    /// </summary>
    public sealed class ClassificationEngine
    {
        private static readonly string[] Categories =
        {
            "Hair", "Tops", "Bottoms", "Full Body", "Shoes", "Accessories",
            "Sliders", "Makeup & Skin", "Poses & Animations", "Furniture",
            "Build Mode", "Electronics", "Lighting", "Deco", "Scripts & Mods"
        };

        private static readonly string[] ScriptMarkers =
        {
            "deviousdesires", "wickedwhims", "wonderfulwhims", "basemental",
            "simcontrolhub", "sim control hub", "situationmanager", "situation manager", "sims4communitylib", "communitylib",
            "xmlinjector", "betterexceptions", "controlanysim", "mc cmd center",
            "mccc", "career", "aspiration", "trait", "buff", "interaction",
            "tuning", "gameplay", "venue", "scenario", "autonomy", "pregnancy",
            "injector", "script", "medic", "remedies", "random underwear distributer",
            "mc woohoo", "customgendersettings", "outdoor retreat overrides"
        };

        private static readonly Dictionary<string, string[]> NameTerms = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Hair"] = new[] { "hair", "hairstyle", "hairstyles", "bangs", "ponytail", "braid", "braids", "locs", "dreads" },
            ["Tops"] = new[] { "top", "tops", "shirt", "shirts", "blouse", "blouses", "jacket", "jackets", "coat", "coats", "sweater", "hoodie", "cardigan", "vest", "tank", "halter", "bra", "corset", "bralette", "bandeau", "lingerie" },
            ["Bottoms"] = new[] { "bottom", "bottoms", "pant", "pants", "jean", "jeans", "skirt", "short", "shorts", "trouser", "trousers", "leggings", "tights", "stockings", "legwarmers", "underwear" },
            ["Full Body"] = new[] { "dress", "dresses", "outfit", "outfits", "jumpsuit", "bodysuit", "swimsuit", "onepiece", "romper", "costume", "uniform" },
            ["Shoes"] = new[] { "shoe", "shoes", "heel", "heels", "boot", "boots", "sneaker", "sneakers", "sandal", "sandals", "slipper", "footwear" },
            ["Accessories"] = new[] { "accessory", "accessories", "necklace", "earring", "earrings", "bracelet", "ring", "headband", "hat", "cap", "glove", "gloves", "choker", "bag", "purse", "piercing", "glasses", "bow", "watch", "belt", "acc" },
            ["Makeup & Skin"] = new[] { "makeup", "lipstick", "lipgloss", "lip", "eyeshadow", "eyeliner", "liner", "blush", "eyebrow", "eyebrows", "eyelash", "eyelashes", "lashes", "skin", "skinblend", "overlay", "freckles", "tattoo", "teeth", "beard", "eyebag", "mole", "nails", "eyes", "contacts", "lenses", "mouthdetails", "boobs", "breast", "breasts", "nipple", "bodypart" },
            ["Sliders"] = new[] { "slider", "sliders", "preset", "presets", "bodypreset", "facepreset", "heightslider" },
            ["Poses & Animations"] = new[] { "pose", "poses", "animation", "animations", "anim", "anims", "dance", "clip", "idle", "walkstyle" },
            ["Furniture"] = new[] { "chair", "sofa", "couch", "bed", "table", "desk", "dresser", "counter", "shelf", "shelves", "cabinet", "bookcase", "furniture", "clutter", "plant", "rug", "mirror", "fridge", "stove", "kitchen", "bathroom", "toilet", "sink", "cooler", "campfire", "object" },
            ["Build Mode"] = new[] { "wallpaper", "wall", "walls", "floor", "flooring", "door", "doors", "window", "windows", "roof", "fence", "stairs", "terrain", "column", "arch", "trim", "foundation", "buildmode" },
            ["Electronics"] = new[] { "phone", "cellphone", "computer", "laptop", "console", "television", "tv", "speaker", "radio" },
            ["Lighting"] = new[] { "lamp", "lighting", "chandelier", "sconce", "ceilinglight", "tablelamp", "walllamp" },
            ["Deco"] = new[] { "deco", "decor", "decoration", "decorations", "ornament", "poster", "painting", "sculpture", "display" }
        };

        private readonly PackageAnalyzer _analyzer = new();

        public ClassificationResult Classify(string filePath)
        {
            var result = new ClassificationResult();
            var scores = Categories.ToDictionary(c => c, _ => 0, StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(filePath)) return result;

            string extension = Path.GetExtension(filePath);
            if (extension.Equals(".ts4script", StringComparison.OrdinalIgnoreCase))
            {
                result.PrimaryCategory = "Scripts & Mods";
                result.Confidence = 100;
                result.NeedsReview = false;
                result.Scores["Scripts & Mods"] = 100;
                result.Evidence.Add(".ts4script extension");
                result.Tags.Add("Script");
                return result;
            }

            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string normalizedName = Normalize(fileName);
            HashSet<string> nameTokens = Tokenize(fileName);
            var directorySegments = GetDirectorySegments(filePath);

            // Exact category folders are strong signals, unlike broad substring matches.
            foreach (string segment in directorySegments)
            {
                var folderTokens = Tokenize(segment);
                foreach (var pair in NameTerms)
                {
                    if (pair.Value.Any(folderTokens.Contains))
                    {
                        Add(scores, pair.Key, 70);
                        result.Evidence.Add($"Folder category: {segment}");
                    }
                }
                if (folderTokens.Contains("scripts") || folderTokens.Contains("script"))
                {
                    Add(scores, "Scripts & Mods", 75);
                    result.Evidence.Add($"Script folder: {segment}");
                }
            }

            // Script/gameplay packages need explicit markers, not the generic word "mod".
            if (ScriptMarkers.Any(marker => ContainsPhrase(normalizedName, marker)))
            {
                Add(scores, "Scripts & Mods", 90);
                result.Evidence.Add("Gameplay/script marker in filename");
                result.Tags.Add("Gameplay");
            }

            foreach (var pair in NameTerms)
            {
                var matched = pair.Value.Where(nameTokens.Contains).ToList();
                if (matched.Count == 0) continue;
                int weight = matched.Count > 1 ? 58 : 48;
                if (pair.Key == "Sliders" || pair.Key == "Full Body" || pair.Key == "Shoes") weight += 5;
                Add(scores, pair.Key, weight);
                result.Evidence.Add($"Filename terms: {string.Join(", ", matched.Take(4))}");
            }

            if (nameTokens.Contains("leg") && nameTokens.Contains("warmer"))
            {
                Add(scores, "Bottoms", 48);
                result.Evidence.Add("Leg warmer garment terms");
            }

            // Nose/face/body geometry names are sliders only when multiple geometry clues agree.
            bool geometryAnchor = nameTokens.Contains("nose") || nameTokens.Contains("nostril") ||
                nameTokens.Contains("jaw") || nameTokens.Contains("chin") || nameTokens.Contains("sidewall") ||
                nameTokens.Contains("torso") || nameTokens.Contains("hips") || nameTokens.Contains("body");
            bool geometryDetail = nameTokens.Contains("height") || nameTokens.Contains("tilt") ||
                nameTokens.Contains("width") || nameTokens.Contains("preset") || nameTokens.Contains("slider");
            if (geometryAnchor && geometryDetail)
            {
                Add(scores, "Sliders", 72);
                result.Evidence.Add("Face/body geometry terms");
            }
            if ((nameTokens.Contains("shape") || nameTokens.Contains("shapes")) &&
                (nameTokens.Contains("body") || nameTokens.Contains("breast") || nameTokens.Contains("face")))
            {
                Add(scores, "Sliders", 58);
                result.Evidence.Add("Body/face shape preset terms");
            }

            // Inspect package resources as supporting evidence only. A generic CAS part
            // cannot distinguish Hair from clothing, skin or accessories.
            if (extension.Equals(".package", StringComparison.OrdinalIgnoreCase))
            {
                var profile = _analyzer.Analyze(filePath);
                if (profile.IsCAS)
                {
                    result.Tags.Add("CAS");
                    result.Evidence.Add($"CAS resources: {profile.CASResources}");
                }
                if (profile.GameplayResources >= 2)
                {
                    Add(scores, "Scripts & Mods", 55);
                    result.Evidence.Add($"Gameplay tuning resources: {profile.GameplayResources}");
                    result.Tags.Add("Gameplay");
                }
                if (profile.ScriptResources > 0)
                {
                    Add(scores, "Scripts & Mods", 60);
                    result.Evidence.Add($"Script resources: {profile.ScriptResources}");
                    result.Tags.Add("Script");
                }
                if (profile.AnimationResources >= 2 && profile.GameplayResources == 0 && !profile.IsCAS)
                {
                    Add(scores, "Poses & Animations", 48);
                    result.Evidence.Add($"Animation resources: {profile.AnimationResources}");
                    result.Tags.Add("Animation");
                }
                if (profile.IsBuildBuy && !profile.IsCAS && profile.GameplayResources == 0 && profile.AnimationResources == 0)
                {
                    result.Evidence.Add($"Build/Buy mesh resources: {profile.MeshResources}; subtype requires filename/folder evidence");
                    result.Tags.Add("BuildBuy");
                }
            }

            result.Scores = scores.Where(s => s.Value > 0)
                .OrderByDescending(s => s.Value)
                .ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);

            if (result.Scores.Count == 0)
            {
                result.PrimaryCategory = "Other";
                result.Confidence = 0;
                result.NeedsReview = true;
                result.Evidence.Add("No reliable category signal");
                return result;
            }

            var ranked = result.Scores.ToList();
            int best = ranked[0].Value;
            int second = ranked.Count > 1 ? ranked[1].Value : 0;
            int margin = best - second;
            result.PrimaryCategory = ranked[0].Key;
            result.Confidence = best >= 85 && margin >= 30 ? 96
                : best >= 65 && margin >= 24 ? 90
                : best >= 50 && margin >= 18 ? 82
                : best >= 40 && margin >= 12 ? 72
                : best >= 30 && margin >= 8 ? 62
                : 45;
            result.NeedsReview = best < 45 || margin < 14 || (second > 0 && second >= best * 0.78);
            if (result.NeedsReview) result.Evidence.Add("Competing or weak signals: manual review recommended");
            return result;
        }

        private static void Add(Dictionary<string, int> scores, string category, int points)
        {
            scores[category] = scores.GetValueOrDefault(category) + points;
        }

        private static List<string> GetDirectorySegments(string filePath)
        {
            var segments = new List<string>();
            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
                while (!string.IsNullOrWhiteSpace(directory))
                {
                    var info = new DirectoryInfo(directory);
                    if (string.IsNullOrWhiteSpace(info.Name)) break;
                    segments.Add(info.Name);
                    directory = info.Parent?.FullName;
                }
            }
            catch { }
            return segments;
        }

        private static string Normalize(string value)
        {
            string separated = Regex.Replace(value ?? string.Empty, "([a-z])([A-Z])", "$1 $2");
            separated = Regex.Replace(separated, "([A-Z])([A-Z][a-z])", "$1 $2");
            return Regex.Replace(separated, "[^A-Za-z0-9]+", " ").Trim().ToLowerInvariant();
        }

        private static HashSet<string> Tokenize(string value)
        {
            return Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static bool ContainsPhrase(string normalizedText, string phrase)
        {
            string haystack = " " + (normalizedText ?? string.Empty).Trim() + " ";
            string needle = " " + Normalize(phrase) + " ";
            return haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }
    }
}
