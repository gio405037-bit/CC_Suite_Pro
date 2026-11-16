using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CCSuitePro.Services
{
    public sealed class CreatorMatch
    {
        public string Name { get; init; } = "Unknown";
        public string Evidence { get; init; } = "None";
    }

    public sealed class CreatorCatalogService
    {
        public sealed class Catalog { public List<CreatorEntry>? Creators { get; set; } }
        public sealed class CreatorEntry
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public List<string>? Aliases { get; set; }
        }
        private sealed class Recognizer
        {
            public string Name { get; init; } = "";
            public List<Regex> Tokens { get; init; } = new();
        }

        private readonly List<Recognizer> _recognizers;

        private CreatorCatalogService(List<Recognizer> recognizers) { _recognizers = recognizers; }

        public static CreatorCatalogService Load()
        {
            try
            {
                var catalogPath = Path.Combine(AppContext.BaseDirectory, "Data", "creators.json");
                if (!File.Exists(catalogPath)) return new CreatorCatalogService(new());
                var catalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(catalogPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var recognizers = new List<Recognizer>();
                foreach (var creator in catalog?.Creators ?? new())
                {
                    if (string.IsNullOrWhiteSpace(creator.Name)) continue;
                    var tokens = new List<Regex>();
                    foreach (var alias in creator.Aliases ?? new())
                    {
                        if (string.IsNullOrWhiteSpace(alias) || alias.Length < 4 || alias.Length > 64) continue;
                        var escaped = Regex.Escape(alias).Replace(@"\ ", @"[\s_.-]+");
                        var pattern = @"(?<![a-zA-Z0-9])" + escaped + @"(?![a-zA-Z0-9])";
                        tokens.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                            TimeSpan.FromMilliseconds(100)));
                    }
                    if (tokens.Count > 0) recognizers.Add(new Recognizer { Name = creator.Name, Tokens = tokens });
                }
                return new CreatorCatalogService(recognizers);
            }
            catch (Exception)
            {
                // A damaged or missing catalog must never prevent the application from scanning mods.
                return new CreatorCatalogService(new());
            }
        }

        private List<string> FindMatches(string text)
        {
            var matches = new List<string>();
            foreach (var creator in _recognizers)
            {
                if (creator.Tokens.Any(token => token.IsMatch(text))) matches.Add(creator.Name);
            }
            return matches;
        }

        public CreatorMatch Identify(string filePath)
        {
            // Filename is the strongest hint. We never infer ownership from the entire Mods path.
            var file = Path.GetFileNameWithoutExtension(filePath);
            var fileMatches = FindMatches(file);
            if (fileMatches.Count == 1) return new CreatorMatch { Name = fileMatches[0], Evidence = "Filename" };
            if (fileMatches.Count > 1) return new CreatorMatch(); // Ambiguous: do not guess.

            // Immediate parent folder only: lower confidence, informational, not eligible for auto-moves.
            var parent = Path.GetFileName(Path.GetDirectoryName(filePath) ?? "");
            var folderMatches = FindMatches(parent);
            return folderMatches.Count == 1
                ? new CreatorMatch { Name = folderMatches[0], Evidence = "Folder" }
                : new CreatorMatch();
        }
    }
}
