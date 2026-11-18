using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CCSuitePro.Services;

// Shared with the independent updater. Private signing material is NEVER stored here.
public static class Winter45UpdateTrust
{
    public const string Repository = "gio405037-bit/CC_Suite_Pro";
    private const string PublicSpkiBase64 = "MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAsSffEUdZzQ6LFoGXQa3abk5YJjQ8ghBP/KhBWy21leck2pjypLynrYC7AhoonJkeJTVJeJyiscfRLPBwg9ockCcaG+0eAQsqp1CRYyXKHuSOUieHpL+0izbkBGWz4N48CKs6rO6hKV82S2bqGMP5KvCfs7/Sgdcf+Jvg3rTQynvE4wFGMDTSslXRXSJ29wUBcB7JzbbHu9XNh6CbKkw/H8X+M1JzEdLL9cxkNCxjL2+mpRwhxe+WGvnJMK+9q57pB526DwBD3PSMkCay8Q0w2mQdnNrPjlvshGQGcA6mSIhAqwFPuGBK+gR+TxHw7/Wh5wtd8qOiBXoV8f/iHMVbReqenvBqKHwLauCUkPWqQi9IOhGdoyzNp7VPuCWDBm7Vw3r2fPb4ueGSyxpBeqbdhM+q4ENjGYzX6OWLUo2SOkjZOCRVGa1DsLzEgBHte1m6LwWRUW0PIaf2PomnlLYRx9aG9wKKFaMrcMls8loUriBOS0WX5LYNsiqdxxfnU5w9AgMBAAE=";
    private static readonly Regex StableTag = new(@"^v(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ForbiddenSegments = new(StringComparer.OrdinalIgnoreCase)
    { "Mods", "ModGuardian", "TimeCapsule", "Scans", "ScanCache", "UserData", "Backup", "Backups", "PatchBackups", "PatchDiagnostics", "Logs", "Tests", "Pruebas", "AI", ".git" };

    public static void ValidateTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || !StableTag.IsMatch(tag))
            throw new InvalidDataException("Release tag must be vMAJOR.MINOR.PATCH");
    }

    public static string ExpectedAssetName(string tag)
    {
        ValidateTag(tag);
        return "CCSuitePro_" + tag + "_win-x64.zip";
    }

    public static string Sha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static byte[] SignedMessage(string tag, string sha256)
    {
        ValidateTag(tag);
        if (!Regex.IsMatch(sha256, "^[A-F0-9]{64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("SHA-256 format invalid");
        return Encoding.UTF8.GetBytes("CCSUITEPRO-UPDATE-V1\n" + Repository + "\n" + tag + "\n" + sha256 + "\n");
    }

    public static void VerifyDetachedSignature(string zip, string tag, string signatureFile)
    {
        if (PublicSpkiBase64.StartsWith("__WINTER45", StringComparison.Ordinal))
            throw new InvalidDataException("Publisher public key has not been configured");
        if (!File.Exists(signatureFile) || new FileInfo(signatureFile).Length > 8192)
            throw new InvalidDataException("Release signature missing or too large");
        string signatureText = File.ReadAllText(signatureFile).Trim();
        byte[] signature;
        try { signature = Convert.FromBase64String(signatureText); }
        catch (FormatException) { throw new InvalidDataException("Release signature is not Base64"); }
        string hash = Sha256(zip);
        byte[] spki = Convert.FromBase64String(PublicSpkiBase64);
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(spki, out int bytesRead);
        if (bytesRead != spki.Length || rsa.KeySize < 3072)
            throw new InvalidDataException("Untrusted publisher public key");
        if (!rsa.VerifyData(SignedMessage(tag, hash), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new InvalidDataException("Publisher signature verification failed");
    }

    // Return normalized relative file paths. No extraction or writes here.
    public static IReadOnlyList<string> ValidateArchive(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count < 10 || archive.Entries.Count > 1500)
            throw new InvalidDataException("Unexpected entry count");
        var paths = new List<string>();
        var uniques = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool exe = false, updater = false, license = false;
        long expanded = 0;
        foreach (var item in archive.Entries)
        {
            string raw = item.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith('/') || raw.Contains(':') || raw.Contains('\0'))
                throw new InvalidDataException("Unsafe ZIP path");
            if (raw.Equals("CCSuitePro/", StringComparison.Ordinal)) continue;
            string[] segments = raw.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2 || segments.Length > 12 ||
                !segments[0].Equals("CCSuitePro", StringComparison.Ordinal) ||
                segments.Any(p => p is "." or ".." || p.Length > 128 || ForbiddenSegments.Contains(p)))
                throw new InvalidDataException("Unsafe or non-application ZIP entry: " + raw);
            int unixMode = (item.ExternalAttributes >> 16) & 0xF000;
            if (unixMode == 0xA000 || ((FileAttributes)item.ExternalAttributes).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Links not allowed in update ZIP");
            if (raw.EndsWith('/')) continue;
            string rel = string.Join('/', segments.Skip(1));
            if (!uniques.Add(rel)) throw new InvalidDataException("Duplicate ZIP entry: " + rel);
            string ext = Path.GetExtension(rel).ToLowerInvariant();
            if (ext is ".package" or ".ts4script" or ".pdb" or ".ps1" or ".bat" or ".cmd" or ".key" or ".pem" or ".pfx")
                throw new InvalidDataException("Forbidden update file type");
            bool allowed = ext is ".dll" or ".exe" or ".png" or ".jpg" or ".ico" or ".dat";
            allowed |= ext == ".json" && (segments.Length == 2 &&
                (rel.Equals("CCSuitePro.deps.json", StringComparison.OrdinalIgnoreCase) ||
                 rel.Equals("CCSuitePro.runtimeconfig.json", StringComparison.OrdinalIgnoreCase)) ||
                segments.Length == 3 && segments[1].Equals("Languages", StringComparison.OrdinalIgnoreCase) ||
                rel.Equals("Data/creators.json", StringComparison.OrdinalIgnoreCase));
            allowed |= rel.Equals("LICENSE", StringComparison.OrdinalIgnoreCase);
            if (!allowed) throw new InvalidDataException("Unexpected file in update: " + rel);
            if (ext == ".exe" && !(rel.Equals("CCSuitePro.exe", StringComparison.OrdinalIgnoreCase) ||
                                     rel.Equals("CCSuitePro.Updater.exe", StringComparison.OrdinalIgnoreCase) ||
                                     rel.Equals("createdump.exe", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unexpected executable: " + rel);
            expanded = checked(expanded + item.Length);
            if (expanded > 4L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Uncompressed update is too large");
            if (item.Length > 0 && item.CompressedLength == 0)
                throw new InvalidDataException("Invalid compression size");
            paths.Add(rel);
            exe |= rel.Equals("CCSuitePro.exe", StringComparison.OrdinalIgnoreCase);
            updater |= rel.Equals("CCSuitePro.Updater.exe", StringComparison.OrdinalIgnoreCase);
            license |= rel.Equals("LICENSE", StringComparison.OrdinalIgnoreCase);
        }
        if (!exe || !updater || !license)
            throw new InvalidDataException("Missing executable, updater, or license");
        return paths;
    }
}
