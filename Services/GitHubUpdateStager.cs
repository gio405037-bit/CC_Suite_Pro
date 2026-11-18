using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CCSuitePro.Services
{
    /// <summary>
    /// Winter 44 - secure download staging only. Does not install, execute or extract.
    /// This verifies integrity against the GitHub Releases asset digest. For a complete
    /// auto-update trust model, a pinned publisher signature must be implemented separately.
    /// </summary>
    public static class GitHubUpdateStager
    {
        private const string ApiPrefix =
            "https://api.github.com/repos/gio405037-bit/CC_Suite_Pro/releases/tags/";
        private const long MaxZipBytes = 1024L * 1024 * 1024;
        private const int MaxMetadataBytes = 1024 * 1024;
        private static readonly Regex TagPattern = new Regex(
            @"^v(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex AssetPattern = new Regex(
            @"^CCSuitePro_[A-Za-z0-9._-]+_win-x64\.zip$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HexPattern = new Regex(
            @"^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(4) };
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("CCSuitePro", "2.0"));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return client;
        }

        public static async Task<string> DownloadAndVerifyAsync(
            string tag, CancellationToken cancellationToken = default)
        {
            // Prevent arbitrary release references and path injection.
            if (string.IsNullOrWhiteSpace(tag) || !TagPattern.IsMatch(tag))
                throw new InvalidDataException("Invalid stable release tag");

            using var metadataResponse = await Client.GetAsync(
                ApiPrefix + Uri.EscapeDataString(tag),
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            metadataResponse.EnsureSuccessStatusCode();
            if (metadataResponse.Content.Headers.ContentLength > MaxMetadataBytes)
                throw new InvalidDataException("Release metadata is too large");

            using var metadataStream = await metadataResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var metadataBuffer = new MemoryStream();
            byte[] chunk = new byte[81920];
            while (true)
            {
                int count = await metadataStream.ReadAsync(chunk.AsMemory(), cancellationToken);
                if (count == 0) break;
                if (metadataBuffer.Length + count > MaxMetadataBytes)
                    throw new InvalidDataException("Release metadata exceeds size limit");
                metadataBuffer.Write(chunk, 0, count);
            }
            metadataBuffer.Position = 0;
            using var json = JsonDocument.Parse(metadataBuffer);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.GetProperty("tag_name").GetString() != tag ||
                root.GetProperty("draft").GetBoolean() ||
                root.GetProperty("prerelease").GetBoolean())
                throw new InvalidDataException("Not a matching published stable release");

            string? assetName = null;
            string? downloadUrl = null;
            string? expectedSha = null;
            long assetSize = 0;
            int matches = 0;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                if (!AssetPattern.IsMatch(name) ||
                    !string.Equals(name, Winter45UpdateTrust.ExpectedAssetName(tag), StringComparison.Ordinal) ||
                    Regex.IsMatch(name, @"preview|debug|test|rc", RegexOptions.IgnoreCase))
                    continue;
                matches++;
                if (matches > 1) break;
                if (!asset.TryGetProperty("digest", out var digestElement) ||
                    digestElement.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Release asset has no SHA-256 digest");
                string digest = digestElement.GetString() ?? "";
                if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
                    !HexPattern.IsMatch(digest.Substring(7)))
                    throw new InvalidDataException("Release asset SHA-256 digest is invalid");
                assetSize = asset.GetProperty("size").GetInt64();
                if (assetSize < 100_000 || assetSize > MaxZipBytes)
                    throw new InvalidDataException("Release asset size outside allowed range");
                assetName = name;
                expectedSha = digest.Substring(7).ToUpperInvariant();
                downloadUrl = asset.GetProperty("browser_download_url").GetString();
            }
            if (matches != 1 || assetName == null || expectedSha == null || downloadUrl == null)
                throw new InvalidDataException("No unique Windows x64 stable ZIP was found");
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                uri.Host != "github.com" ||
                !uri.AbsolutePath.StartsWith(
                    "/gio405037-bit/CC_Suite_Pro/releases/download/",
                    StringComparison.OrdinalIgnoreCase) ||
                uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
                throw new InvalidDataException("Release download URL is not trusted");

            // This is updater-owned storage, never the Mods folder or game files.
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CCSuitePro", "Updates", "Staged", tag);
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, assetName);
            if (File.Exists(destination))
            {
                if (VerifyFileHash(destination, expectedSha) && VerifyZipStructure(destination))
                    return destination;
                throw new InvalidDataException("A different file already exists in update staging");
            }
            string partial = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".part");
            try
            {
                using var response = await Client.GetAsync(
                    uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxZipBytes)
                    throw new InvalidDataException("Download is too large");
                using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long total = 0;
                using (var target = new FileStream(partial, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    while (true)
                    {
                        int count = await source.ReadAsync(chunk.AsMemory(), cancellationToken);
                        if (count == 0) break;
                        total += count;
                        if (total > MaxZipBytes)
                            throw new InvalidDataException("Download exceeds size limit");
                        hash.AppendData(chunk, 0, count);
                        await target.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
                    }
                    await target.FlushAsync(cancellationToken);
                }
                if (total != assetSize ||
                    Convert.ToHexString(hash.GetHashAndReset()) != expectedSha)
                    throw new InvalidDataException("Downloaded ZIP did not match published SHA-256");
                if (!VerifyZipStructure(partial))
                    throw new InvalidDataException("Downloaded ZIP has an unsafe or incomplete structure");
                File.Move(partial, destination);
                return destination;
            }
            finally
            {
                try { if (File.Exists(partial)) File.Delete(partial); }
                catch (IOException) { /* leave for manual cleanup */ }
            }
        }

        private static bool VerifyFileHash(string path, string expectedSha)
        {
            using var file = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(file)) == expectedSha;
        }

        private static bool VerifyZipStructure(string path)
        {
            try
            {
                using var archive = ZipFile.OpenRead(path);
                if (archive.Entries.Count < 2 || archive.Entries.Count > 1500) return false;
                bool hasExecutable = false;
                bool hasLicense = false;
                long expandedBytes = 0;
                foreach (var item in archive.Entries)
                {
                    string name = item.FullName.Replace('\\', '/');
                    if (name.StartsWith("/", StringComparison.Ordinal) ||
                        name.Contains(':') ||
                        name.Split('/').Any(p => p == "..")) return false;
                    expandedBytes += item.Length;
                    if (expandedBytes > 4L * MaxZipBytes) return false;
                    string[] parts = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Any(p => p.Equals("Mods", StringComparison.OrdinalIgnoreCase))) return false;
                    if (name.EndsWith(".package", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".ts4script", StringComparison.OrdinalIgnoreCase)) return false;
                    if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) return false;
                    if (name.Equals("CCSuitePro.exe", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("CCSuitePro/CCSuitePro.exe", StringComparison.OrdinalIgnoreCase))
                        hasExecutable = true;
                    if (name.Equals("LICENSE", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("CCSuitePro/LICENSE", StringComparison.OrdinalIgnoreCase))
                        hasLicense = true;
                }
                return hasExecutable && hasLicense;
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
        }
    }
}
