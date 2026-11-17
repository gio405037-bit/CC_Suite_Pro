using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CCSuitePro.Services
{
    // Winter 43: consulta manual, solo lectura. Nunca descarga ni ejecuta archivos.
    public enum Winter43ReleaseState
    {
        NoPublishedRelease,
        UpToDate,
        UpdateAvailable,
        UnsupportedVersion
    }

    public sealed class Winter43ReleaseResult
    {
        public Winter43ReleaseState State { get; }
        public string Tag { get; }

        public Winter43ReleaseResult(Winter43ReleaseState state, string tag = "")
        {
            State = state;
            Tag = tag;
        }
    }

    public static class GitHubReleaseChecker
    {
        private const string ReleasesListApi =
            "https://api.github.com/repos/gio405037-bit/CC_Suite_Pro/releases?per_page=50";

        public const string ReleasesPage =
            "https://github.com/gio405037-bit/CC_Suite_Pro/releases";

        private static readonly HttpClient Client = CreateClient();
        private static readonly Regex StableTag = new Regex(
            @"^[vV]?(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("CCSuitePro", "2.0"));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return client;
        }

        public static string InstalledVersion
        {
            get
            {
                Version current = typeof(GitHubReleaseChecker).Assembly.GetName().Version
                    ?? new Version(2, 0, 0, 0);
                return $"{current.Major}.{current.Minor}.{Math.Max(current.Build, 0)}";
            }
        }

        public static async Task<Winter43ReleaseResult> CheckLatestStableAsync(
            CancellationToken cancellationToken = default)
        {
            // Consulta publica, manual y de solo lectura. No descarga ni ejecuta archivos.
            // La Release antigua "CC-Suite-Pro" no es SemVer, pero no debe impedir
            // reconocer versiones modernas como v2.0.0 o v2.1.0.
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesListApi);
            using var response = await Client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new Winter43ReleaseResult(Winter43ReleaseState.NoPublishedRelease);
            if (response.StatusCode == HttpStatusCode.Forbidden ||
                (int)response.StatusCode == 429)
                throw new HttpRequestException("GitHub API rate limit or access denied");

            response.EnsureSuccessStatusCode();
            const int maxJsonBytes = 4 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maxJsonBytes)
                throw new InvalidDataException("Release metadata is too large");

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                int read = await stream.ReadAsync(
                    buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;
                if (memory.Length + read > maxJsonBytes)
                    throw new InvalidDataException("Release metadata exceeds limit");
                memory.Write(buffer, 0, read);
            }
            memory.Position = 0;
            using var json = JsonDocument.Parse(memory);
            if (json.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Unexpected releases metadata");

            Version? newestSupported = null;
            string newestTag = "";
            bool foundLegacyStable = false;

            foreach (JsonElement release in json.RootElement.EnumerateArray())
            {
                if (release.ValueKind != JsonValueKind.Object) continue;
                if ((release.TryGetProperty("draft", out var draft) &&
                     draft.ValueKind == JsonValueKind.True) ||
                    (release.TryGetProperty("prerelease", out var prerelease) &&
                     prerelease.ValueKind == JsonValueKind.True)) continue;
                if (!release.TryGetProperty("tag_name", out var tagProperty) ||
                    tagProperty.ValueKind != JsonValueKind.String) continue;

                var tag = tagProperty.GetString() ?? "";
                var match = StableTag.Match(tag);
                if (!match.Success ||
                    !int.TryParse(match.Groups["major"].Value, out var major) ||
                    !int.TryParse(match.Groups["minor"].Value, out var minor) ||
                    !int.TryParse(match.Groups["patch"].Value, out var patch))
                {
                    foundLegacyStable = true;
                    continue;
                }
                var version = new Version(major, minor, patch);
                if (newestSupported == null || version.CompareTo(newestSupported) > 0)
                {
                    newestSupported = version;
                    newestTag = $"v{major}.{minor}.{patch}";
                }
            }

            if (newestSupported == null)
                return new Winter43ReleaseResult(foundLegacyStable
                    ? Winter43ReleaseState.UnsupportedVersion
                    : Winter43ReleaseState.NoPublishedRelease);

            var assemblyVersion = typeof(GitHubReleaseChecker).Assembly.GetName().Version
                ?? new Version(2, 0, 0, 0);
            var installed = new Version(assemblyVersion.Major, assemblyVersion.Minor,
                Math.Max(assemblyVersion.Build, 0));
            return new Winter43ReleaseResult(
                newestSupported.CompareTo(installed) > 0
                    ? Winter43ReleaseState.UpdateAvailable
                    : Winter43ReleaseState.UpToDate,
                newestTag);
        }
    }
}
