using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CCSuitePro.Services;

public static class Winter45SignedRelease
{
    private static readonly HttpClient Client = NewClient();
    private static HttpClient NewClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CCSuitePro", "2.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
    private static bool IsTrustedDownload(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            return false;
        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.StartsWith("/gio405037-bit/CC_Suite_Pro/releases/download/", StringComparison.OrdinalIgnoreCase);
        return uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> ReadLimited(HttpContent content, int cap, CancellationToken ct)
    {
        using var input = await content.ReadAsStreamAsync(ct);
        using var target = new MemoryStream();
        byte[] buf = new byte[4096];
        while (true)
        {
            int n = await input.ReadAsync(buf.AsMemory(), ct);
            if (n == 0) break;
            if (target.Length + n > cap) throw new InvalidDataException("HTTP response exceeds size limit");
            target.Write(buf, 0, n);
        }
        return target.ToArray();
    }

    public static async Task<string> FetchAndVerifyAsync(string zipFile, string tag, CancellationToken ct = default)
    {
        Winter45UpdateTrust.ValidateTag(tag);
        string filename = Winter45UpdateTrust.ExpectedAssetName(tag);
        if (!Path.GetFileName(zipFile).Equals(filename, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Staged file name does not match the release tag");
        // The original Winter44 module already checked the GitHub digest. Enforce a separate
        // independent publisher signature here, AND in the external installer.
        using var response = await Client.GetAsync(
            "https://api.github.com/repos/" + Winter45UpdateTrust.Repository + "/releases/tags/" + tag,
            HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 1024 * 1024) throw new InvalidDataException("Metadata oversized");
        byte[] meta = await ReadLimited(response.Content, 1024 * 1024, ct);
        if (meta.Length > 1024 * 1024) throw new InvalidDataException("Metadata oversized");
        using var json = JsonDocument.Parse(meta);
        var root = json.RootElement;
        if (root.GetProperty("tag_name").GetString() != tag || root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("Not a stable release");
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        string signatureName = filename + ".sig";
        var signatures = assets.Where(a => string.Equals(a.GetProperty("name").GetString(), signatureName, StringComparison.Ordinal)).ToArray();
        if (signatures.Length != 1) throw new InvalidDataException("Signed release asset is missing or ambiguous");
        if (signatures[0].GetProperty("size").GetInt64() > 8192) throw new InvalidDataException("Signature oversized");
        string? link = signatures[0].GetProperty("browser_download_url").GetString();
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || !IsTrustedDownload(uri))
            throw new InvalidDataException("Untrusted signature download link");
        // Follow only known GitHub asset delivery hosts, with a strict response-size limit.
        byte[]? bytes = null;
        for (int redirects = 0; redirects < 4; redirects++)
        {
            using var r = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (r.StatusCode == HttpStatusCode.Found || r.StatusCode == HttpStatusCode.Redirect || r.StatusCode == HttpStatusCode.TemporaryRedirect)
            {
                Uri? next = r.Headers.Location;
                if (next is null) throw new InvalidDataException("Missing download redirect");
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                if (!IsTrustedDownload(uri)) throw new InvalidDataException("Untrusted download redirect");
                continue;
            }
            r.EnsureSuccessStatusCode();
            if (r.Content.Headers.ContentLength > 8192) throw new InvalidDataException("Signature oversized");
            bytes = await ReadLimited(r.Content, 8192, ct);
            break;
        }
        if (bytes == null || bytes.Length == 0 || bytes.Length > 8192)
            throw new InvalidDataException("Signature unavailable");
        string sidecar = zipFile + ".sig";
        string part = sidecar + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await File.WriteAllBytesAsync(part, bytes, ct);
            Winter45UpdateTrust.VerifyDetachedSignature(zipFile, tag, part);
            Winter45UpdateTrust.ValidateArchive(zipFile);
            File.Move(part, sidecar, true);
            return sidecar;
        }
        finally { try { if (File.Exists(part)) File.Delete(part); } catch (IOException) { } }
    }
}
