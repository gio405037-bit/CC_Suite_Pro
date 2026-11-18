using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using CCSuitePro.Services;

internal static class Program
{
    internal sealed class Transaction
    {
        public Transaction() { }
        public string InstallDirectory { get; set; } = "";
        public string State { get; set; } = "Prepared";
        public List<Item> Files { get; set; } = new();
        public sealed class Item
        {
            public string Relative { get; set; } = "";
            public bool Existed { get; set; }
        }
    }
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 5 && args[0] == "apply")
            {
                Apply(args[1], args[2], args[3], int.Parse(args[4]));
                return 0;
            }
            if (args.Length == 4 && args[0] == "verify")
            {
                Winter45UpdateTrust.VerifyDetachedSignature(args[1], args[2], args[3]);
                Winter45UpdateTrust.ValidateArchive(args[1]);
                Console.WriteLine("SIGNED_PACKAGE_OK");
                return 0;
            }
            if (args.Length == 3 && args[0] == "rollback")
            {
                Restore(args[1], args[2]);
                return 0;
            }
            throw new ArgumentException("Use: apply <zip> <tag> <appDir> <parentPid>; verify <zip> <tag> <sig>; rollback <backupDir> <appDir>");
        }
        catch (Exception error)
        {
            try
            {
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCSuitePro", "Updates", "Logs");
                Directory.CreateDirectory(logDir);
                File.AppendAllText(Path.Combine(logDir, "updater.log"), DateTime.UtcNow.ToString("o") + " " + error.GetType().Name + ": " + error.Message + Environment.NewLine);
            }
            catch { /* never fail reporting a failed update */ }
            Console.Error.WriteLine("UPDATE_ABORTED: " + error.GetType().Name + ": " + error.Message);
            return 1;
        }
    }
    private static void CheckSafeDirectory(string appDir)
    {
        if (!Path.IsPathFullyQualified(appDir)) throw new InvalidDataException("Non-absolute app path");
        var full = Path.GetFullPath(appDir).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetPathRoot(full)!.TrimEnd(Path.DirectorySeparatorChar).Equals(full, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cannot update a volume root");
        if (!File.Exists(Path.Combine(full, "CCSuitePro.exe"))) throw new InvalidDataException("Not an existing CC Suite Pro installation");
        var path = new DirectoryInfo(full);
        while (path != null)
        {
            if (path.Name.Equals("Mods", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Cannot update inside Mods folder");
            if ((path.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Directory link not permitted");
            path = path.Parent;
        }
    }
    private static void WaitForParent(int pid)
    {
        if (pid <= 0) throw new InvalidDataException("Invalid parent PID");
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (!proc.WaitForExit(45000)) throw new IOException("CC Suite Pro did not close in time");
        }
        catch (ArgumentException) { /* already closed */ }
    }
    private static string BackupRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCSuitePro", "Updates", "Backups");
    private static void SaveTransaction(string path, Transaction txn) =>
        File.WriteAllText(Path.Combine(path, "transaction.json"), JsonSerializer.Serialize(txn, new JsonSerializerOptions { WriteIndented = true }));
    private static void CheckNoLink(string root, string relative)
    {
        string final = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!final.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Path escapes installation directory");
        string? walker = Path.GetDirectoryName(final);
        while (walker != null && walker.Length >= root.Length)
        {
            if (Directory.Exists(walker) && (File.GetAttributes(walker) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Directory link encountered");
            if (walker.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
            walker = Path.GetDirectoryName(walker);
        }
        if (File.Exists(final) && (File.GetAttributes(final) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("File link encountered");
    }
    private static void Apply(string zip, string tag, string installDir, int parentPid)
    {
        string app = Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar);
        CheckSafeDirectory(app);
        Winter45UpdateTrust.ValidateTag(tag);
        if (!Path.GetFileName(zip).Equals(Winter45UpdateTrust.ExpectedAssetName(tag), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ZIP name / tag mismatch");
        Winter45UpdateTrust.VerifyDetachedSignature(zip, tag, zip + ".sig");
        var files = Winter45UpdateTrust.ValidateArchive(zip);
        WaitForParent(parentPid);
        foreach (var other in Process.GetProcessesByName("CCSuitePro"))
        {
            using (other)
            {
                try { if (!other.HasExited) throw new IOException("Another CC Suite Pro instance is still running"); }
                catch (InvalidOperationException) { /* instance exited while checking */ }
            }
        }
        // Verify AGAIN after the parent exits to prevent a stale/tampered staged package.
        Winter45UpdateTrust.VerifyDetachedSignature(zip, tag, zip + ".sig");
        CheckSafeDirectory(app);
        string backup = Path.Combine(BackupRoot(), DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        string scratch = Path.Combine(Path.GetTempPath(), "CCSuitePro_W45_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var transaction = new Transaction { InstallDirectory = app };
        try
        {
            using (var archive = ZipFile.OpenRead(zip))
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith('/')) continue;
                    string rel = entry.FullName.Replace('\\', '/')["CCSuitePro/".Length..];
                    if (!seen.Add(rel)) throw new InvalidDataException("Duplicate file");
                    string extracted = Path.Combine(scratch, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(extracted)!);
                    using var from = entry.Open();
                    using var to = new FileStream(extracted, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    from.CopyTo(to);
                    if (to.Length != entry.Length) throw new InvalidDataException("ZIP entry truncated");
                }
            }
            foreach (string rel in files)
            {
                CheckNoLink(app, rel);
                string current = Path.Combine(app, rel.Replace('/', Path.DirectorySeparatorChar));
                bool existed = File.Exists(current);
                transaction.Files.Add(new Transaction.Item { Relative = rel, Existed = existed });
                if (existed)
                {
                    string old = Path.Combine(backup, "original", rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(old)!);
                    File.Copy(current, old, false);
                }
            }
            SaveTransaction(backup, transaction);
            transaction.State = "Replacing";
            SaveTransaction(backup, transaction);
            foreach (string rel in files)
            {
                CheckNoLink(app, rel);
                string current = Path.Combine(app, rel.Replace('/', Path.DirectorySeparatorChar));
                string incoming = Path.Combine(scratch, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(current)!);
                string temporary = current + ".w45_" + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.Copy(incoming, temporary, false); File.Move(temporary, current, true); }
                finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
            }
            // Start the new app and revert if Windows cannot start it, or it crashes immediately.
            using var started = Process.Start(new ProcessStartInfo(Path.Combine(app, "CCSuitePro.exe"))
            { WorkingDirectory = app, UseShellExecute = true });
            if (started == null) throw new IOException("Could not relaunch the application");
            if (started.WaitForExit(8000)) throw new IOException("The updated application exited immediately");
            transaction.State = "Committed";
            SaveTransaction(backup, transaction);
            Console.WriteLine("UPDATE_COMMITTED. BACKUP: " + backup);
        }
        catch
        {
            if (transaction.State == "Replacing")
            {
                try { Restore(backup, app); }
                catch (Exception e) { Console.Error.WriteLine("AUTOMATIC_ROLLBACK_FAILED: " + e.Message + "; backup: " + backup); }
            }
            throw;
        }
        finally { try { Directory.Delete(scratch, true); } catch (IOException) { } }
    }
    private static void Restore(string backup, string appDir)
    {
        string app = Path.GetFullPath(appDir).TrimEnd(Path.DirectorySeparatorChar);
        CheckSafeDirectory(app);
        string root = Path.GetFullPath(backup).TrimEnd(Path.DirectorySeparatorChar);
        if (!root.StartsWith(Path.GetFullPath(BackupRoot()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Untrusted rollback directory");
        string journal = Path.Combine(root, "transaction.json");
        if (!File.Exists(journal)) throw new FileNotFoundException("Rollback journal missing");
        var txn = JsonSerializer.Deserialize<Transaction>(File.ReadAllText(journal)) ?? throw new InvalidDataException("Invalid rollback journal");
        if (!txn.InstallDirectory.Equals(app, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Backup belongs to a different app installation");
        // Never delete unknown files or modify other installation/user-data paths.
        foreach (var item in txn.Files.AsEnumerable().Reverse())
        {
            if (item.Relative.Contains("..") || Path.IsPathRooted(item.Relative)) throw new InvalidDataException("Unsafe rollback entry");
            CheckNoLink(app, item.Relative);
            string target = Path.Combine(app, item.Relative.Replace('/', Path.DirectorySeparatorChar));
            if (item.Existed)
            {
                string source = Path.Combine(root, "original", item.Relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(source)) throw new FileNotFoundException("Rollback backup missing", source);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, true);
            }
            else if (File.Exists(target)) File.Delete(target);
        }
        txn.State = "RolledBack";
        SaveTransaction(root, txn);
        Console.WriteLine("ROLLBACK_COMPLETED");
    }
}
