using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CCSuitePro.Models;

namespace CCSuitePro.Services
{
    // Private, offline filename-only model; works without Python on another PC.
    // ConfirmedCategory is ALWAYS manually supplied. Never edits/modifies any mod files.
    // Suggestions are advisory and can only increase NeedsReview (never auto-organize).
    public sealed class PythonSuggestionService
    {
        private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
        {
            "Hair", "Tops", "Bottoms", "Full Body", "Shoes", "Accessories",
            "Sliders", "Makeup & Skin", "Poses & Animations", "Furniture", "Build Mode",
            "Electronics", "Lighting", "Deco", "Scripts & Mods"
        };
        private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
        {
            "package", "ts4script", "sims", "the", "cc", "mod", "v1", "v2"
        };
        // Public release feature gate: experimental AI remains in source for private development.
        // A public user cannot enable it through settings or saved CSV labels.
        public const bool ReleaseAiEnabled = false;

        private static string ReleaseDisabledStatus => LocalText(
            "Experimental AI is disabled in this release while we improve it.",
            "La IA experimental está desactivada en esta versión mientras seguimos mejorándola.",
            "실험적 AI 기능은 개선 중이므로 이번 버전에서는 비활성화되었습니다.");

        // Forget only AI-derived display suggestions, not classification, MD5, review flags,
        // scanned file list or any personal labels / cached files on disk.
        private static void ClearVisibleSuggestions(IEnumerable<ModFile> files)
        {
            foreach (var file in files)
            {
                file.AISuggestedCategory = string.Empty;
                file.AIScore = 0;
                file.AIExplanation = string.Empty;
            }
        }

        private const int MinimumLabels = 12;
        private const int MinimumPerCategory = 4;
        private const int MaxLabelRows = 20000;
        private const long MaxCsvBytes = 4 * 1024 * 1024;

        public static string LabelsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CCSuitePro", "AI", "labels.csv");

        public static string InitialStatus => ReleaseAiEnabled ? LocalText(
            "Native classifier ready. After scanning, review labels to train optional offline AI.",
            "Clasificador integrado listo. Tras analizar, revisa etiquetas para entrenar la IA local opcional.",
            "기본 분류기 준비 완료. 스캔 후 레이블을 검토하면 선택형 오프라인 AI를 학습할 수 있습니다.") : ReleaseDisabledStatus;


        // Zero-training fallback: deterministic filename clues, NOT a trained model.
        // Abstains on unknown, conflicting or ambiguous names; never reads or edits mod bytes.
        // Category and organizer assignments remain unchanged; suggestions are advisory only.
        private static readonly Dictionary<string, string[]> BuiltInFilenameHints = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Hair"] = new[] { "hair", "hairstyle", "hairstyles", "wig", "wigs", "bangs", "braids", "ponytail" },
            ["Tops"] = new[] { "shirt", "shirts", "blouse", "hoodie", "sweater", "tshirt", "tshirts", "cardigan", "jacket" },
            ["Bottoms"] = new[] { "pants", "jeans", "shorts", "skirt", "skirts", "leggings" },
            ["Full Body"] = new[] { "dress", "dresses", "gown", "gowns", "jumpsuit", "romper", "outfit" },
            ["Shoes"] = new[] { "shoes", "heels", "sneakers", "boots", "sandals", "slippers" },
            ["Accessories"] = new[] { "earrings", "necklace", "bracelet", "glasses", "hat", "hats", "piercing", "purse" },
            ["Sliders"] = new[] { "slider", "sliders" },
            ["Makeup & Skin"] = new[] { "makeup", "lipstick", "eyeliner", "eyeshadow", "blush", "eyelashes", "lashes", "skinblend" },
            ["Poses & Animations"] = new[] { "poses", "posepack", "animation", "animations" },
            ["Furniture"] = new[] { "sofa", "couch", "armchair", "chairs", "tables", "beds", "dresser", "cabinets" },
            ["Build Mode"] = new[] { "wallpaper", "flooring", "windows", "doors", "staircase", "fences" },
            ["Electronics"] = new[] { "television", "computer", "laptop", "stereo" },
            ["Lighting"] = new[] { "lamp", "lamps", "chandelier", "sconce", "lighting" },
            ["Deco"] = new[] { "clutter", "sculpture", "painting", "paintings", "vase", "vases" }
        };

        private static void ApplyZeroTrainingHints(IEnumerable<ModFile> files)
        {
            foreach (var file in files)
            {
                if (file.IsScript) continue;
                var tokens = Tokens(file.FileName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (tokens.Length == 0) continue;
                var matches = BuiltInFilenameHints
                    .Select(kv => (Category: kv.Key, Terms: tokens.Where(t => kv.Value.Contains(t, StringComparer.OrdinalIgnoreCase)).ToArray()))
                    .Where(x => x.Terms.Length > 0)
                    .OrderByDescending(x => x.Terms.Length)
                    .ToList();
                if (matches.Count == 0) continue;
                // A single clear clue is useful; a tie across categories is not.
                if (matches.Count > 1 && matches[0].Terms.Length <= matches[1].Terms.Length) continue;
                string suggestion = matches[0].Category;
                int confidence = Math.Min(84, 72 + (matches[0].Terms.Length - 1) * 6);
                file.AISuggestedCategory = suggestion;
                file.AIScore = confidence; // Heuristic strength, not a statistical probability.
                string evidence = string.Join(", ", matches[0].Terms.Take(4));
                file.AIExplanation = LocalText("Filename evidence: ", "Evidencia en el nombre: ", "파일명 근거: ") + evidence;
                if (!suggestion.Equals(file.Category, StringComparison.OrdinalIgnoreCase))
                {
                    file.NeedsReview = true; // More conservative; never changes actual category.
                    file.AIExplanation += LocalText("; manual review required.",
                        "; requiere revisión manual.", "; 수동 검토가 필요합니다.");
                }
            }
        }

        private static string BaselineStatus(IEnumerable<ModFile> files, int confirmedLabels = 0)
        {
            int suggestions = files.Count(f => f.AIScore > 0 && !string.IsNullOrWhiteSpace(f.AISuggestedCategory));
            return LocalText(
                $"Offline filename suggestions: {suggestions} (no training needed). Optional AI labels: {confirmedLabels}/12; check each suggestion manually.",
                $"Sugerencias básicas sin entrenamiento: {suggestions}. Etiquetas opcionales de IA: {confirmedLabels}/12. Revisa cada sugerencia.",
                $"학습 없이 사용할 수 있는 파일명 제안: {suggestions}개. 선택형 AI 레이블: {confirmedLabels}/12. 제안을 직접 확인하세요.");
        }

        // Localization for a saved scan or a language change; no training or file changes.
        public static string GetCurrentStatus(IEnumerable<ModFile> files)
        {
            if (!ReleaseAiEnabled)
            {
                ClearVisibleSuggestions(files);
                return ReleaseDisabledStatus;
            }
            if (!File.Exists(LabelsPath))
                return BaselineStatus(files);
            List<(string Name, string Category)> labels;
            try { labels = LoadConfirmedLabels(); }
            catch (Exception)
            {
                return BaselineStatus(files) + " " + LocalText(
                    "Review CSV cannot be read.", "No se pudo leer el CSV de entrenamiento.",
                    "학습용 CSV를 읽을 수 없습니다.");
            }
            int distinctCategories = labels.GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                .Count(g => g.Count() >= MinimumPerCategory);
            if (labels.Count < MinimumLabels || distinctCategories < 2)
                return BaselineStatus(files, labels.Count);
            int suggestions = files.Count(f => f.AIScore > 0 && !string.IsNullOrWhiteSpace(f.AISuggestedCategory));
            return LocalText(
                $"Native classifier ACTIVE. Local AI trained with {labels.Count} confirmed labels; {suggestions} suggestions. Review before acting.",
                $"Clasificador integrado ACTIVO. IA local entrenada con {labels.Count} etiquetas confirmadas; {suggestions} sugerencias. Revísalas antes de actuar.",
                $"기본 분류기 활성화됨. 로컬 AI는 확인된 레이블 {labels.Count}개로 학습했고 {suggestions}개를 제안했습니다. 적용 전 검토하세요.");
        }

        // Translate only known, generated explanations; leave custom/unrecognized text intact.
        public static string LocalizeExplanation(ModFile file)
        {
            if (!ReleaseAiEnabled) return string.Empty;
            string original = file.AIExplanation ?? string.Empty;
            if (original.Length == 0) return original;
            if (file.IsScript && (original.StartsWith("Script: ", StringComparison.Ordinal) ||
                original.StartsWith("스크립트: ", StringComparison.Ordinal)))
                return LocalText("Script: keep the installation structure.",
                    "Script: conserva la estructura de instalación.", "스크립트: 설치 구조를 유지하세요.");
            string[] prefixes = { "Filename evidence: ", "Evidencia en el nombre: ", "파일명 근거: " };
            string[] suffixes = { "; manual review required.", "; requiere revisión manual.",
                "; requiere revisión manual.", "; 수동 검토가 필요합니다." };
            foreach (string prefix in prefixes)
            {
                if (!original.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string evidence = original.Substring(prefix.Length);
                foreach (string suffix in suffixes)
                    if (evidence.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        evidence = evidence.Substring(0, evidence.Length - suffix.Length);
                        break;
                    }
                return LocalText("Filename evidence: ", "Evidencia en el nombre: ", "파일명 근거: ") + evidence +
                    (file.NeedsReview ? LocalText("; manual review required.",
                        "; requiere revisión manual.", "; 수동 검토가 필요합니다.") : string.Empty);
            }
            if (original.StartsWith("Native classifier: ", StringComparison.Ordinal) ||
                original.StartsWith("Clasificador integrado: ", StringComparison.Ordinal) ||
                original.StartsWith("기본 분류기: ", StringComparison.Ordinal))
                return LocalText("Native classifier: manual review recommended.",
                    "Clasificador integrado: se recomienda revisión manual.",
                    "기본 분류기: 수동 검토를 권장합니다.");
            if (original.StartsWith("Native classifier suggestion; ", StringComparison.Ordinal) ||
                original.StartsWith("Sugerencia del clasificador integrado; ", StringComparison.Ordinal) ||
                original.StartsWith("기본 분류기 제안이며 ", StringComparison.Ordinal))
                return LocalText("Native classifier suggestion; not a compatibility check.",
                    "Sugerencia del clasificador integrado; no verifica compatibilidad.",
                    "기본 분류기 제안이며 호환성 검사가 아닙니다.");
            return original;
        }

        public Task<string> ApplySuggestionsAsync(List<ModFile> files)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (!ReleaseAiEnabled)
            {
                ClearVisibleSuggestions(files);
                return Task.FromResult(ReleaseDisabledStatus);
            }
            // Work off the UI thread; never contact any network or start other processes.
            return Task.Run(() => ApplySuggestions(files));
        }

        private static string LocalText(string en, string es, string ko)
        {
            return LanguageService.Instance.CurrentLanguage switch
            {
                "es" => es,
                "ko" => ko,
                _ => en
            };
        }

        private static List<string> Tokens(string name)
        {
            name = Regex.Replace(name ?? string.Empty, "([a-z])([A-Z])", "$1 $2")
                .ToLowerInvariant();
            return Regex.Matches(name, "[a-z0-9]+")
                .Cast<Match>()
                .Select(m => m.Value)
                .Where(x => x.Length >= 3 && !Stopwords.Contains(x))
                .ToList();
        }

        // Same single-line quoted CSV representation as ExportAIReviews(); no external library.
        private static List<string> CsvFields(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else current.Append(c);
            }
            if (quoted) throw new InvalidDataException("Unclosed CSV quote.");
            result.Add(current.ToString());
            return result;
        }

        private static List<(string Name, string Category)> LoadConfirmedLabels()
        {
            var result = new List<(string Name, string Category)>();
            var file = new FileInfo(LabelsPath);
            if (!file.Exists) return result;
            if (file.Length > MaxCsvBytes) throw new InvalidDataException("AI review CSV too large.");

            using var reader = new StreamReader(file.FullName, Encoding.UTF8, true);
            string? header = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(header)) return result;
            var columns = CsvFields(header.TrimStart('\uFEFF'));
            int filenameIndex = columns.FindIndex(x => x.Equals("FileName", StringComparison.OrdinalIgnoreCase));
            int confirmedIndex = columns.FindIndex(x => x.Equals("ConfirmedCategory", StringComparison.OrdinalIgnoreCase));
            if (filenameIndex < 0 || confirmedIndex < 0)
                throw new InvalidDataException("AI review CSV missing required columns.");
            string? line;
            while ((line = reader.ReadLine()) != null && result.Count < MaxLabelRows)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = CsvFields(line);
                if (filenameIndex >= fields.Count || confirmedIndex >= fields.Count) continue;
                string name = fields[filenameIndex].Trim();
                string category = fields[confirmedIndex].Trim();
                if (name.Length > 0 && name.Length <= 260 && Allowed.Contains(category))
                    result.Add((name, category));
            }
            return result;
        }

        private static string ApplySuggestions(List<ModFile> files)
        {
            foreach (var f in files)
            {
                f.AISuggestedCategory = string.Empty;
                f.AIScore = 0;
                f.AIExplanation = f.IsScript
                    ? LocalText("Script: keep the installation structure.",
                        "Script: conserva su estructura de instalación.",
                        "스크립트: 설치 구조를 유지하세요.")
                    : f.NeedsReview
                        ? LocalText("Native classifier: manual review recommended.",
                            "Clasificador integrado: se recomienda revisión manual.",
                            "기본 분류기: 수동 검토를 권장합니다.")
                        : LocalText("Native classifier suggestion; not a compatibility check.",
                            "Sugerencia del clasificador integrado; no verifica compatibilidad.",
                            "기본 분류기 제안이며 호환성 검사가 아닙니다.");
            }

            ApplyZeroTrainingHints(files);

            if (!File.Exists(LabelsPath))
                return BaselineStatus(files);

            List<(string Name, string Category)> labels;
            try { labels = LoadConfirmedLabels(); }
            catch (Exception)
            {
                return BaselineStatus(files) + " " + LocalText(
                    "Review CSV cannot be read.", "No se pudo leer el CSV de entrenamiento.",
                    "학습용 CSV를 읽을 수 없습니다.");
            }

            var classCount = labels.GroupBy(l => l.Category, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var active = classCount.Where(kv => kv.Value >= MinimumPerCategory)
                .Select(kv => kv.Key).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            if (labels.Count < MinimumLabels || active.Count < 2)
                return BaselineStatus(files, labels.Count);

            var wordsByClass = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            var wordsTotal = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var vocabulary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string category in active)
            {
                wordsByClass[category] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                wordsTotal[category] = 0;
            }
            foreach (var label in labels)
            {
                if (!wordsByClass.TryGetValue(label.Category, out var words)) continue;
                foreach (string term in Tokens(label.Name))
                {
                    words.TryGetValue(term, out int n);
                    words[term] = n + 1;
                    wordsTotal[label.Category]++;
                    vocabulary.Add(term);
                }
            }
            int examples = active.Sum(c => classCount[c]);
            int suggested = 0;
            foreach (var f in files)
            {
                if (f.IsScript) continue; // Never recommend reclassifying scripts.
                var known = Tokens(f.FileName).Where(vocabulary.Contains).ToList();
                if (known.Count == 0) continue;
                var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (string category in active)
                {
                    double score = Math.Log((classCount[category] + 1d) / (examples + active.Count));
                    int denominator = wordsTotal[category] + Math.Max(1, vocabulary.Count);
                    foreach (var term in known)
                    {
                        wordsByClass[category].TryGetValue(term, out int count);
                        score += Math.Log((count + 1d) / denominator);
                    }
                    scores[category] = score;
                }
                var ranked = scores.OrderByDescending(kv => kv.Value).ToList();
                double margin = ranked[0].Value - ranked[1].Value;
                double maxScore = ranked[0].Value;
                double sum = scores.Values.Sum(v => Math.Exp(v - maxScore));
                double ratio = 1d / sum;
                if (ratio < 0.88 || margin < 1.6) continue;

                f.AISuggestedCategory = ranked[0].Key;
                f.AIScore = Math.Min(100, Math.Max(88, (int)Math.Round(ratio * 100d)));
                string evidence = string.Join(", ", known.Distinct(StringComparer.OrdinalIgnoreCase).Take(4));
                f.AIExplanation = LocalText(
                    "Filename evidence: " + evidence,
                    "Evidencia en el nombre: " + evidence,
                    "파일명 근거: " + evidence);
                if (!ranked[0].Key.Equals(f.Category, StringComparison.OrdinalIgnoreCase))
                {
                    f.NeedsReview = true;
                    f.AIExplanation += LocalText("; manual review required.",
                        "; requiere revisión manual.", "; 수동 검토가 필요합니다.");
                }
                suggested++;
            }
            suggested = files.Count(f => f.AIScore > 0 && !string.IsNullOrWhiteSpace(f.AISuggestedCategory));
            return LocalText(
                $"Native classifier ACTIVE. Local AI trained with {labels.Count} confirmed labels; {suggested} suggestions. Review before acting.",
                $"Clasificador integrado ACTIVO. IA local entrenada con {labels.Count} etiquetas; {suggested} sugerencias. Revísalas antes de actuar.",
                $"기본 분류기 사용 가능. 로컬 AI: 확인된 레이블 {labels.Count}개로 학습, 제안 {suggested}개. 적용 전 검토하세요.");
        }
    }
}
