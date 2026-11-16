using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CCSuitePro.Models;
using CCSuitePro.Helpers;
using CCSuitePro.Services;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
        Console.WriteLine("PASS " + message);
    }

    private static byte[] PackageBytes()
    {
        var bytes = new byte[96];
        Encoding.ASCII.GetBytes("DBPF").CopyTo(bytes, 0);
        for (int i = 4; i < bytes.Length; i++) bytes[i] = (byte)(i % 13);
        // Valid-looking header for non-destructive scanner tests, not a playable mod.
        bytes[32] = bytes[33] = bytes[34] = bytes[35] = 0;
        return bytes;
    }

    public static async Task<int> Main(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "CCSuite_ReleaseSmoke_" + Guid.NewGuid().ToString("N"));
        var mods = Path.Combine(root, "Isolated_Mods");
        var backup = Path.Combine(root, "Backups");
        var quarantine = Path.Combine(root, "Quarantine");
        Directory.CreateDirectory(mods);
        try
        {
            var one = Path.Combine(mods, "MAKEUP_Lipstick_A.package");
            var two = Path.Combine(mods, "MAKEUP_Lipstick_B.package");
            var broken = Path.Combine(mods, "SUSPICIOUS_BAD.package");
            var cached = Path.Combine(mods, "test.cache");
            var payload = PackageBytes();
            File.WriteAllBytes(one, payload);
            File.WriteAllBytes(two, payload);
            File.WriteAllBytes(broken, new byte[] { 0x00, 0x01, 0x02 });
            File.WriteAllText(cached, "disposable cache test file");
            var originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(one)));

            // A single saved scan survives a restart, is overwritten on refresh, and
            // stays isolated from other folders and the application distribution.
            var scanCache = Path.Combine(root, "ScanCache", "last_scan.json");
            var savedFiles1 = new List<ModFile>
            {
                new ModFile { FilePath = one, FileSize = 96, MD5Hash = new string('a', 32), Category = "Makeup & Skin" },
                new ModFile { FilePath = two, FileSize = 96, MD5Hash = new string('a', 32), Category = "Makeup & Skin" }
            };
            var savedDuplicates = new List<DuplicateGroup>
            {
                new DuplicateGroup { MD5Hash = new string('a', 32), Files = savedFiles1 }
            };
            Check(LastScanCacheService.TrySaveToPath(scanCache, mods, savedFiles1,
                savedDuplicates, new List<ConflictResult>(), "test", out _),
                "Last scan saved to one isolated local snapshot");
            Check(LastScanCacheService.TryLoadFromPath(scanCache, mods, out var loaded1) &&
                loaded1.Files.Count == 2 && loaded1.DuplicateGroups.Count == 1,
                "Last scan restores the saved file list and duplicate groups");
            Check(LastScanCacheService.TrySaveToPath(scanCache, mods,
                new List<ModFile> { savedFiles1[0] }, new List<DuplicateGroup>(),
                new List<ConflictResult>(), "rescan", out _),
                "Rescan atomically overwrites the previous snapshot");
            Check(LastScanCacheService.TryLoadFromPath(scanCache, mods, out var loaded2) &&
                loaded2.Files.Count == 1 && loaded2.DuplicateGroups.Count == 0 &&
                loaded2.AIStatus == "rescan", "New scan does not retain old entries");
            Check(Directory.GetFiles(Path.GetDirectoryName(scanCache)!, "*").Length == 1,
                "Only one snapshot file exists; no scan history accumulates");
            var differentFolder = Path.Combine(root, "Other_Mods");
            Directory.CreateDirectory(differentFolder);
            Check(!LastScanCacheService.TryLoadFromPath(scanCache, differentFolder, out _),
                "Scan cache cannot be loaded for another Mods folder");
            Check(LastScanCacheService.TryClearFromPath(scanCache, out _) && !File.Exists(scanCache),
                "Clearing the list deletes only its cached scan");
            File.WriteAllText(scanCache, "{not a valid saved scan", Encoding.UTF8);
            Check(!LastScanCacheService.TryLoadFromPath(scanCache, mods, out _),
                "Malformed saved scan is ignored without stopping startup");
            Check(LastScanCacheService.TryClearFromPath(scanCache, out _),
                "Corrupt saved scan can be removed safely");

            var scanner = new ModScannerService();
            var files = await scanner.ScanDirectoryAsync(mods);
            Check(files.Count == 3, "Scanner discovers three package files");
            Check(files.All(f => f.MD5Hash.Length == 32), "Scanner retains MD5 hashes");
            var duplicateService = new DuplicateDetectorService(quarantine);
            var duplicates = duplicateService.FindDuplicates(files);
            Check(duplicates.Count == 1 && duplicates[0].Files.Count == 2,
                "Duplicate detection confirms exactly one identical pair");

            await new BackupService().CreateFullBackupAsync(mods, backup);
            var archives = Directory.GetFiles(backup, "Backup_*.zip");
            Check(archives.Length == 1, "Verified backup ZIP created outside Mods");
            using (var zip = ZipFile.OpenRead(archives[0]))
            {
                var backupEntry = zip.GetEntry("MAKEUP_Lipstick_A.package");
                Check(backupEntry != null, "Backup includes original package");
                if (backupEntry != null)
                {
                    using var archivedBytes = backupEntry.Open();
                    var archivedHash = Convert.ToHexString(SHA256.HashData(archivedBytes));
                    Check(archivedHash == originalHash,
                        "Backup ZIP preserves package bytes by SHA-256");
                }
            }

            // Regression: a fresh scan must not retain a path that disappeared from disk.
            var transient = Path.Combine(mods, "TRANSIENT_REMOVED.package");
            File.WriteAllBytes(transient, PackageBytes());
            var scanWithTransient = await scanner.ScanDirectoryAsync(mods);
            Check(scanWithTransient.Any(f => f.FilePath == transient),
                "Scanner sees a newly added package on a fresh scan");
            File.Delete(transient);
            var scanAfterRemoval = await scanner.ScanDirectoryAsync(mods);
            Check(!scanAfterRemoval.Any(f => f.FilePath == transient),
                "Fresh scan does not return a removed package");

            var cleaner = new CleanerService(quarantine);
            var junk = cleaner.ScanForJunk(mods);
            Check(junk.Any(x => x.Category == "Cache Files" && x.FilesFound == 1),
                "Cleaner detects temporary cache");
            Check(junk.Any(x => x.Category == "Corrupted Files" && x.Items.Any(i => i.FilePath == broken)),
                "Cleaner lists malformed package candidates for individual review");
            var suspect = junk.First(x => x.Category == "Corrupted Files");
            cleaner.CleanCategory(mods, suspect);
            Check(File.Exists(broken), "Clean All path never removes suspicious package automatically");
            string brokenHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(broken)));
            var brokenIssue = suspect.Items.Single(i => i.FilePath == broken);
            var brokenAction = cleaner.QuarantineCorruptedPackage(mods, brokenIssue);
            Check(brokenAction.Success && !File.Exists(broken) && File.Exists(brokenAction.QuarantinePath),
                "User-approved corrupt package action moves the file to quarantine");
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(brokenAction.QuarantinePath))) == brokenHash,
                "Corrupt package quarantine preserves original bytes by SHA-256");

            var staleBroken = Path.Combine(mods, "STALE_CORRUPT.package");
            File.WriteAllBytes(staleBroken, new byte[] { 0x01, 0x02, 0x03 });
            var staleIssue = cleaner.ScanForJunk(mods).First(x => x.Category == "Corrupted Files").Items.Single(i => i.FilePath == staleBroken);
            File.WriteAllBytes(staleBroken, PackageBytes());
            var staleCorruptAction = cleaner.QuarantineCorruptedPackage(mods, staleIssue);
            Check(!staleCorruptAction.Success && File.Exists(staleBroken),
                "Corrupt quarantine refuses a package repaired after the scan");
            var cacheResult = junk.First(x => x.Category == "Cache Files");
            cleaner.CleanCategory(mods, cacheResult);
            Check(!File.Exists(cached) && Directory.GetFiles(quarantine, "test.cache", SearchOption.AllDirectories).Length == 1,
                "Cleaner quarantines disposable cache without deleting its bytes");

            // Duplicate action: one selected copy can be quarantined while another exact copy remains.
            var duplicateA = Path.Combine(mods, "DUPLICATE_KEEP.package");
            var duplicateB = Path.Combine(mods, "DUPLICATE_REMOVE.package");
            File.WriteAllBytes(duplicateA, PackageBytes());
            File.WriteAllBytes(duplicateB, PackageBytes());
            var duplicateScan = await scanner.ScanDirectoryAsync(mods);
            var duplicateGroup = duplicateService.FindDuplicates(duplicateScan)
                .Single(g => g.Files.Any(f => f.FilePath == duplicateA) && g.Files.Any(f => f.FilePath == duplicateB));
            string duplicateHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(duplicateB)));
            var duplicateAction = duplicateService.QuarantineDuplicateFile(mods, duplicateGroup,
                duplicateGroup.Files.Single(f => f.FilePath == duplicateB));
            Check(duplicateAction.Success && !File.Exists(duplicateB) && File.Exists(duplicateA),
                "Selected duplicate is quarantined while an identical copy remains in Mods");
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(duplicateAction.QuarantinePath))) == duplicateHash,
                "Duplicate quarantine preserves original bytes by SHA-256");

            var groupA = Path.Combine(mods, "GROUP_A_OLD.package");
            var groupB = Path.Combine(mods, "GROUP_B_MIDDLE.package");
            var groupNewest = Path.Combine(mods, "GROUP_C_NEWEST.package");
            var groupPayload = PackageBytes(); groupPayload[95] ^= 0x6B;
            File.WriteAllBytes(groupA, groupPayload);
            File.WriteAllBytes(groupB, groupPayload);
            File.WriteAllBytes(groupNewest, groupPayload);
            File.SetLastWriteTimeUtc(groupA, DateTime.UtcNow.AddDays(-2));
            File.SetLastWriteTimeUtc(groupB, DateTime.UtcNow.AddDays(-1));
            File.SetLastWriteTimeUtc(groupNewest, DateTime.UtcNow);
            var groupScan = await scanner.ScanDirectoryAsync(mods);
            var groupToClean = duplicateService.FindDuplicates(groupScan)
                .Single(g => g.Files.Any(f => f.FilePath == groupA) && g.Files.Any(f => f.FilePath == groupB) && g.Files.Any(f => f.FilePath == groupNewest));
            var groupAction = duplicateService.QuarantineDuplicateCopies(mods, groupToClean, keepNewest: true);
            Check(groupAction.Success && File.Exists(groupNewest) && !File.Exists(groupA) && !File.Exists(groupB) && groupAction.FilesQuarantined == 2,
                "Group action keeps the newest file and quarantines every extra identical copy");

            // Integrity regression: matching MD5 metadata must not bypass byte confirmation.
            var fakeHashA = Path.Combine(mods, "COLLISION_A.package");
            var fakeHashB = Path.Combine(mods, "COLLISION_B.package");
            File.WriteAllBytes(fakeHashA, PackageBytes());
            var collisionBytes = PackageBytes(); collisionBytes[90] ^= 0x5A;
            File.WriteAllBytes(fakeHashB, collisionBytes);
            var fabricated = new List<ModFile>
            {
                new ModFile { FilePath = fakeHashA, FileSize = collisionBytes.Length, MD5Hash = new string('A', 32), LastModified = File.GetLastWriteTime(fakeHashA) },
                new ModFile { FilePath = fakeHashB, FileSize = collisionBytes.Length, MD5Hash = new string('A', 32), LastModified = File.GetLastWriteTime(fakeHashB) }
            };
            Check(new DuplicateDetectorService().FindDuplicates(fabricated).Count == 0,
                "Different bytes with matching hash metadata are not reported as duplicates");
            // The destructive quarantine method must also revalidate the bytes at action time.
            var hypothetical = new DuplicateGroup { MD5Hash = new string('A', 32), Files = fabricated };
            var staleDuplicateAction = duplicateService.QuarantineDuplicateCopies(mods, hypothetical);
            Check(!staleDuplicateAction.Success && File.Exists(fakeHashA) && File.Exists(fakeHashB),
                "Changed files are never quarantined solely on stale duplicate metadata");

            // Classification fixtures must exist in the isolated test Mods folder. Keep
            // their bytes distinct so they cannot interfere with duplicate/quarantine tests.
            var classifierFixtureNames = new[]
            {
                "deviousdesires.package",
                "DQuiet_SimControlHub_SituationManager.package",
                "Ice-CreamForBreakfast_TS4NoseAlarSidewallHeightNostrilTiltUpdated.package",
                "RidgeportVelourTracksuitJacket.package",
                "S-AllureOR_SwimsuitOnePiece.package",
                "123456_unknown_asset.package"
            };
            for (int i = 0; i < classifierFixtureNames.Length; i++)
            {
                var fixtureBytes = PackageBytes();
                fixtureBytes[90] ^= (byte)(i + 1);
                File.WriteAllBytes(Path.Combine(mods, classifierFixtureNames[i]), fixtureBytes);
            }

            var classifier = new ClassificationEngine();
            Check(classifierFixtureNames.All(name => File.Exists(Path.Combine(mods, name))),
                "Classifier fixtures exist in the isolated Mods folder");
            Check(classifier.Classify(Path.Combine(mods, "deviousdesires.package")).PrimaryCategory == "Scripts & Mods",
                "Classifier recognizes Devious Desires packages as gameplay mods, not Hair");
            Check(classifier.Classify(Path.Combine(mods, "DQuiet_SimControlHub_SituationManager.package")).PrimaryCategory == "Scripts & Mods",
                "Classifier recognizes Sim Control Hub package as a gameplay mod");
            Check(classifier.Classify(Path.Combine(mods, "Ice-CreamForBreakfast_TS4NoseAlarSidewallHeightNostrilTiltUpdated.package")).PrimaryCategory == "Sliders",
                "Classifier recognizes face geometry sliders from combined filename evidence");
            Check(classifier.Classify(Path.Combine(mods, "RidgeportVelourTracksuitJacket.package")).PrimaryCategory == "Tops",
                "Classifier recognizes jackets as Tops");
            Check(classifier.Classify(Path.Combine(mods, "S-AllureOR_SwimsuitOnePiece.package")).PrimaryCategory == "Full Body",
                "Classifier recognizes one-piece swimsuits as Full Body");
            var unknownClassification = classifier.Classify(Path.Combine(mods, "123456_unknown_asset.package"));
            Check(unknownClassification.PrimaryCategory == "Other" && unknownClassification.NeedsReview,
                "Classifier leaves ambiguous assets in Other for human review");
            Check(classifier.Classify(Path.Combine(mods, "anything.ts4script")).PrimaryCategory == "Scripts & Mods",
                "Every .ts4script file is classified as Scripts & Mods");
            var missingPackageClassification = classifier.Classify(Path.Combine(mods, "deviousdesires_missing.package"));
            Check(missingPackageClassification.PrimaryCategory == "Scripts & Mods",
                "Classifier uses filename evidence safely when a package disappears");

            // Use explicit, reviewed categories in a disposable folder, so the test does not
            // rely on uncertain filename classification or touch a user's real library.
            var targetFiles = files.Where(f => f.FilePath == one || f.FilePath == two).ToList();
            foreach (var f in targetFiles)
            {
                f.Category = "Makeup & Skin";
                f.NeedsReview = false;
            }
            var organizer = new OrganizerService();
            var plan = organizer.BuildPlan(targetFiles, mods);
            Check(plan.Count == 2, "Safe organizer generates the expected preview");
            await organizer.ExecutePlanAsync(plan);
            var moved = Path.Combine(mods, "Makeup and Skin", Path.GetFileName(one));
            Check(File.Exists(moved) && !File.Exists(one), "Organizer moves approved CC only");
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(moved))) == originalHash,
                "Organizer preserves package bytes and resource identifiers");

            // Reject a stale preview without touching any file.
            var staleFile = Path.Combine(mods, "MAKEUP_Stale.package");
            File.WriteAllBytes(staleFile, payload);
            var stale = new ModFile
            {
                FilePath = staleFile, FileSize = payload.Length, Category = "Makeup & Skin",
                LastModified = File.GetLastWriteTime(staleFile), NeedsReview = false
            };
            var stalePlan = organizer.BuildPlan(new List<ModFile> { stale }, mods);
            Check(stalePlan.Count == 1, "Stale preview test prepares a move");
            File.AppendAllText(staleFile, "changed after preview");
            bool refused = false;
            try { await organizer.ExecutePlanAsync(stalePlan); }
            catch (IOException) { refused = true; }
            Check(refused && File.Exists(staleFile), "Organizer rejects a changed file and leaves it untouched");

            if (args.Length == 2 && args[0] == "--stress")
            {
                string corpus = Path.GetFullPath(args[1]);
                var synthetic = Directory.GetFiles(corpus, "SYNTHETIC_READONLY_*.package");
                Check(synthetic.Length == 5, "Stress corpus contains five synthetic packages");
                foreach (var path in synthetic)
                {
                    var copied = Path.Combine(mods, Path.GetFileName(path));
                    File.Copy(path, copied);
                    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) ==
                          Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copied))),
                          "Stress fixture copied without byte changes");
                }
                var stressScan = await scanner.ScanDirectoryAsync(mods);
                Check(stressScan.Count >= 8, "Stress scan finds all synthetic packages without exceptions");
                Check(stressScan.Where(f => f.FileName.StartsWith("SYNTHETIC_READONLY_"))
                    .All(f => f.MD5Hash.Length == 32), "Stress scan hashes synthetic corpus successfully");
            }

            Console.WriteLine($"SUCCESS: {_assertions} checks passed. Only disposable test paths were used.");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}