using CCSuitePro.Models;
using CCSuitePro.Services;
using Newtonsoft.Json;
using System.IO;

int passed = 0;
void Check(bool condition, string test)
{
    if (!condition) throw new Exception("FAIL: " + test);
    Console.WriteLine("PASS: " + test);
    passed++;
}
string root = Path.Combine(Path.GetTempPath(), "CCSuitePro_WinterSmoke_" + Guid.NewGuid().ToString("N"));
string working = Path.Combine(root, "Mods");
Directory.CreateDirectory(working);
try
{
    ModFile File(string name, int size, string hash) => new()
    {
        FilePath = Path.Combine(working, name), FileSize = size, MD5Hash = hash,
        Category = "Hair", LastModified = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc)
    };
    var a = File("Creator_Hair.package", 8, "111");
    var b = File("Creator_Hair2.package", 8, "111");
    var snap = new LastScanCacheService.Snapshot
    {
        FolderPath = working, ScannedAtUtc = DateTimeOffset.UtcNow,
        Files = new List<ModFile> { a, b },
        DuplicateGroups = new List<DuplicateGroup> { new() { MD5Hash = "111", Files = new() { a, b } } }
    };
    var cap = WinterLibraryService.MakeCapsule(snap);
    Check(cap.Total == 2 && cap.Packages == 2 && cap.DuplicateGroups == 1, "Capsule aggregates scan");
    string json = JsonConvert.SerializeObject(cap);
    Check(!json.Contains("Creator_Hair") && !json.Contains(working) && !json.Contains("111"), "Capsule does not include personal file data");
    string capsulesFile = Path.Combine(root, "private", "capsules.json");
    WinterLibraryService.SaveCapsule(cap, capsulesFile);
    var capsules = WinterLibraryService.LoadCapsules(capsulesFile);
    Check(capsules.Count == 1 && capsules[0].Total == 2, "Capsule history saved and restored");
    var before = WinterLibraryService.CreateBaseline(snap);
    Check(before.Files.Count == 2 && before.FolderFingerprint.Length == 64, "Guardian baseline uses folder fingerprint");
    string guardianDir = Path.Combine(root, "private", "Guardian");
    WinterLibraryService.SaveBaseline(working, before, guardianDir);
    var saved = WinterLibraryService.LoadBaseline(working, guardianDir);
    Check(saved != null && saved.Files.Count == 2, "Guardian baseline saved in test directory");
    var changed = File("Creator_Hair.package", 12, "222");
    var added = File("Other_Shoes.package", 3, "333");
    var afterSnap = new LastScanCacheService.Snapshot
    {
        FolderPath = working, ScannedAtUtc = DateTimeOffset.UtcNow.AddSeconds(1),
        Files = new List<ModFile> { changed, added }
    };
    var after = WinterLibraryService.CreateBaseline(afterSnap);
    var report = WinterLibraryService.Compare(saved!, after);
    Check(report.Added == 1 && report.Changed == 1 && report.Missing == 1,
        "Guardian finds new/changed/not-found files without changing Mods");
    Check(Directory.GetFiles(working).Length == 0, "Smoke never writes game Mod files");
    bool rejected = false;
    try
    {
        var other = File("Unrelated.package", 4, "abc");
        other.FilePath = Path.Combine(root, "Outside.package");
        var invalid = new LastScanCacheService.Snapshot
        {
            FolderPath = working, ScannedAtUtc = DateTimeOffset.UtcNow,
            Files = new List<ModFile> { other }
        };
        WinterLibraryService.CreateBaseline(invalid);
    }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "Guardian rejects files outside selected folder");
    Console.WriteLine("SUCCESS: " + passed + " Winter Edition checks passed using disposable test paths.");
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}
