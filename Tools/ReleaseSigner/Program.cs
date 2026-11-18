using System.Security.Cryptography;
using System.Text;
using CCSuitePro.Services;

internal static class Program
{
    private static string ReadPassword(string label)
    {
        string? env = Environment.GetEnvironmentVariable("CCSUITE_SIGN_PASSPHRASE");
        if (!string.IsNullOrWhiteSpace(env)) return env; // CI secrets only
        Console.Write(label);
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
            if (key.Key == ConsoleKey.Backspace && chars.Count > 0) { chars.RemoveAt(chars.Count - 1); continue; }
            if (!char.IsControl(key.KeyChar)) chars.Add(key.KeyChar);
        }
        return new string(chars.ToArray());
    }
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "init")
            {
                if (File.Exists(args[1]) || File.Exists(args[2])) throw new IOException("Signing key already exists; refusing to overwrite");
                string pass = ReadPassword("Create a signing passphrase (16+ characters): ");
                if (pass.Length < 16) throw new ArgumentException("Use at least 16 characters");
                if (pass != ReadPassword("Repeat passphrase: ")) throw new ArgumentException("Passphrases differ");
                using var rsa = RSA.Create(3072);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
                string encrypted = rsa.ExportEncryptedPkcs8PrivateKeyPem(pass, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 250_000));
                File.WriteAllText(args[1], encrypted, new UTF8Encoding(false));
                File.WriteAllText(args[2], Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), new UTF8Encoding(false));
                Console.WriteLine("SIGNING_KEY_CREATED_LOCALLY_PRIVATE_NOT_UPLOADED");
                return 0;
            }
            if (args.Length == 5 && args[0] == "sign")
            {
                string zip = args[1], tag = args[2], privatePemFile = args[3], sigOutput = args[4];
                Winter45UpdateTrust.ValidateTag(tag);
                if (!Path.GetFileName(zip).Equals(Winter45UpdateTrust.ExpectedAssetName(tag), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Name mismatch");
                Winter45UpdateTrust.ValidateArchive(zip);
                using var rsa = RSA.Create();
                rsa.ImportFromEncryptedPem(File.ReadAllText(privatePemFile), ReadPassword("Signing passphrase: "));
                byte[] signature = rsa.SignData(Winter45UpdateTrust.SignedMessage(tag, Winter45UpdateTrust.Sha256(zip)), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                File.WriteAllText(sigOutput, Convert.ToBase64String(signature) + "\n", Encoding.ASCII);
                Console.WriteLine("SIGNED_PACKAGE_CREATED");
                return 0;
            }
            if (args.Length == 4 && args[0] == "verify")
            {
                Winter45UpdateTrust.VerifyDetachedSignature(args[1], args[2], args[3]);
                Winter45UpdateTrust.ValidateArchive(args[1]);
                Console.WriteLine("SIGNED_PACKAGE_OK");
                return 0;
            }
            throw new ArgumentException("Usage: init private.enc.pem public.b64 | sign zip tag private.enc.pem zip.sig | verify zip tag zip.sig");
        }
        catch (Exception e) { Console.Error.WriteLine("SIGNING_FAILED: " + e.Message); return 1; }
    }
}
