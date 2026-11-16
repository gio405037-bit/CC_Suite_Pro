// Winter Edition 36 — tests functionales sin datos personales.
// All writes are confined to the single disposable directory passed by the caller.
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CCSuitePro.Models;
using CCSuitePro.Services;
using Newtonsoft.Json;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
    throw new ArgumentException("Expected one disposable QA directory argument.");
string root = Path.GetFullPath(args[0]);
string sandboxName = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
if (!sandboxName.StartsWith("CCSuitePro_W36_SANDBOX_", StringComparison.Ordinal))
    throw new InvalidOperationException("Unsafe QA directory name. Refusing to start.");
if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("QA directory must be inside Windows Temp.");
if (Directory.Exists(root)) throw new InvalidOperationException("QA sandbox already exists.");
Directory.CreateDirectory(root);
string fakeMods = Path.Combine(root, "Synthetic_Mods");
string privateData = Path.Combine(root, "QA_PrivateData");
Directory.CreateDirectory(fakeMods);
Directory.CreateDirectory(privateData);

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    checks++;
}
void Reject(Action action, string name)
{
    bool rejected = false;
    try { action(); }
    catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException)
    { rejected = true; }
    Check(rejected, name);
}

// Files are fake text test fixtures, never real game Mods.
string alpha = Path.Combine(fakeMods, "SyntheticAlpha.package");
string beta = Path.Combine(fakeMods, "SyntheticBeta.package");
string gamma = Path.Combine(fakeMods, "SyntheticGamma.ts4script");
File.WriteAllText(alpha, "winter34 alpha fixture", Encoding.UTF8);
File.WriteAllText(beta, "winter34 beta fixture", Encoding.UTF8);
File.WriteAllText(gamma, "winter34 gamma fixture", Encoding.UTF8);
Dictionary<string, string> InitialContents() => Directory.GetFiles(fakeMods, "*", SearchOption.AllDirectories)
    .ToDictionary(f => Path.GetRelativePath(fakeMods, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))), StringComparer.OrdinalIgnoreCase);
var original = InitialContents();
var scanTime = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
ModFile Mod(string name, long size, string hash, string category = "Hair", DateTime? changed = null) => new()
{
    FilePath = Path.Combine(fakeMods, name), FileSize = size, MD5Hash = hash,
    Category = category, LastModified = changed ?? new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc)
};
var m1 = Mod("SyntheticAlpha.package", 19, "AAA");
var m2 = Mod("SyntheticBeta.package", 20, "AAA", "Creator/Unsafe");
var m3 = Mod("SyntheticGamma.ts4script", 21, "CCC", "Scripts");
LastScanCacheService.Snapshot Scan(List<ModFile> files, string? folder = null) => new()
{
    FolderPath = folder ?? fakeMods,
    ScannedAtUtc = scanTime,
    Files = files
};
var first = new LastScanCacheService.Snapshot
{
    FolderPath = fakeMods, ScannedAtUtc = scanTime,
    Files = new List<ModFile> { m1, m2, m3 },
    DuplicateGroups = new List<DuplicateGroup> { new() { MD5Hash = "AAA", Files = new() { m1, m2 } } }
};

string capsuleFile = Path.Combine(privateData, "Capsules", "history.json");
string baselineDir = Path.Combine(privateData, "Guardian");
Check(WinterLibraryService.LoadCapsules(capsuleFile).Count == 0, "01 Empty capsule history");
var capsule = WinterLibraryService.MakeCapsule(first);
Check(capsule.Total == 3 && capsule.Packages == 2 && capsule.Scripts == 1 && capsule.Bytes == 60,
    "02 Capsule totals, extensions and size");
Check(capsule.DuplicateGroups == 1 && capsule.Categories.ContainsKey("Other") && !capsule.Categories.ContainsKey("Creator/Unsafe"),
    "03 Duplicate statistics and sanitized category");
string capsuleJson = JsonConvert.SerializeObject(capsule);
Check(!capsuleJson.Contains("SyntheticAlpha") && !capsuleJson.Contains("SyntheticBeta") &&
      !capsuleJson.Contains("SyntheticGamma") && !capsuleJson.Contains(fakeMods) && !capsuleJson.Contains("AAA"),
    "04 Capsule JSON excludes names, paths and hashes");
WinterLibraryService.SaveCapsule(capsule, capsuleFile);
Check(File.Exists(capsuleFile) && WinterLibraryService.LoadCapsules(capsuleFile).Count == 1,
    "05 Private capsule saved and restored");
Check(!Directory.GetFiles(Path.GetDirectoryName(capsuleFile)!, "*.tmp").Any(),
    "06 No orphan atomic temporary file");
var early = new WinterLibraryService.Capsule { CreatedUtc = capsule.CreatedUtc.AddDays(-10), Total = 1 };
WinterLibraryService.SaveCapsule(early, capsuleFile);
var ordered = WinterLibraryService.LoadCapsules(capsuleFile);
Check(ordered.Count == 2 && ordered[0].Id == capsule.Id,
    "07 Capsule history sorted newest first");
Reject(() => WinterLibraryService.SaveCapsule(new WinterLibraryService.Capsule { Total = -1 }, capsuleFile),
    "08 Invalid capsule cannot be saved");
Reject(() => WinterLibraryService.MakeCapsule(Scan(new List<ModFile> { Mod("X.package", -10, "x") })),
    "09 Negative scanned file size rejected");
Reject(() => WinterLibraryService.ValidateScan(Scan(Enumerable.Repeat(m1, 150001).ToList())),
    "10 Excessive scan file count rejected");
var manyCapsFile = Path.Combine(privateData, "Capsules", "bounded.json");
for (int i = 0; i < 122; i++)
    WinterLibraryService.SaveCapsule(new WinterLibraryService.Capsule { Id = "cap" + i, CreatedUtc = scanTime.AddSeconds(i), Total = i }, manyCapsFile);
var bounded = WinterLibraryService.LoadCapsules(manyCapsFile);
Check(bounded.Count == 120 && bounded[0].Id == "cap121" && bounded[^1].Id == "cap2",
    "11 History retains 120 latest capsules");
var corruptCapsuleFile = Path.Combine(privateData, "Capsules", "corrupt.json");
File.WriteAllText(corruptCapsuleFile, "{ broken json", Encoding.UTF8);
Reject(() => { _ = WinterLibraryService.LoadCapsules(corruptCapsuleFile); },
    "12 Corrupted capsule JSON rejected");

var before = WinterLibraryService.CreateBaseline(first);
Check(before.Files.Count == 3 && before.FolderFingerprint.Length == 64 &&
      before.Files.All(f => !Path.IsPathRooted(f.Relative)) && before.DuplicateGroups == 1,
    "13 Guardian baseline contains relative files and fingerprint");
Check(WinterLibraryService.LoadBaseline(fakeMods, baselineDir) == null,
    "14 Missing Guardian reference returns null");
string expectedBaseline = WinterLibraryService.GetGuardianPath(fakeMods, baselineDir);
WinterLibraryService.SaveBaseline(fakeMods, before, baselineDir);
Check(File.Exists(expectedBaseline) && WinterLibraryService.LoadBaseline(fakeMods, baselineDir) is { Files.Count: 3 },
    "15 Guardian saves and restores only in explicit QA directory");
var saved = WinterLibraryService.LoadBaseline(fakeMods, baselineDir)!;
Check(!File.ReadAllText(expectedBaseline).Contains(fakeMods),
    "16 Guardian private JSON excludes absolute Mods folder path");
Check(WinterLibraryService.GetGuardianPath(fakeMods, baselineDir) == expectedBaseline &&
      WinterLibraryService.GetGuardianPath(Path.Combine(root, "Other"), baselineDir) != expectedBaseline,
    "17 Fingerprint deterministically separates libraries");

var same = WinterLibraryService.Compare(saved, WinterLibraryService.CreateBaseline(first));
Check(same.Added == 0 && same.Changed == 0 && same.Missing == 0 && same.DuplicateGroupDelta == 0,
    "18 Unchanged library has zero differences");
var changed = Mod("SyntheticAlpha.package", 19, "DIFFERENT"); // equal size, new hash
var added = Mod("NewSynthetic.package", 4, "NEW");
var second = Scan(new List<ModFile> { changed, m3, added });
var after = WinterLibraryService.CreateBaseline(second);
var report = WinterLibraryService.Compare(saved, after);
Check(report.Added == 1 && report.Changed == 1 && report.Missing == 1,
    "19 Guardian detects added, modified and missing files");
Check(report.Changes.Any(c => c.Kind == "Added" && c.Relative == "NewSynthetic.package") &&
      report.Changes.Any(c => c.Kind == "Changed" && c.Relative == "SyntheticAlpha.package") &&
      report.Changes.Any(c => c.Kind == "Missing" && c.Relative == "SyntheticBeta.package"),
    "20 Guardian assigns correct change categories");
Check(report.CurrentDuplicateGroups == 0 && report.DuplicateGroupDelta == -1,
    "21 Guardian reports duplicate group delta");
var sameHashCase = Mod("SyntheticAlpha.package", 19, "aaa");
var caseSnap = Scan(new List<ModFile> { sameHashCase, m2, m3 });
Check(WinterLibraryService.Compare(saved, WinterLibraryService.CreateBaseline(caseSnap)).Changed == 0,
    "22 Hash comparison is case-insensitive");
var noHash = Mod("SyntheticAlpha.package", 19, "", changed: new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc));
var noHashSnap = Scan(new List<ModFile> { noHash, m2, m3 });
Check(WinterLibraryService.Compare(saved, WinterLibraryService.CreateBaseline(noHashSnap)).Changed == 1,
    "23 Timestamp fallback detects changes without hashes");
var differentFolder = Path.Combine(root, "DifferentSyntheticFolder");
var wrongLib = Scan(new List<ModFile>(), differentFolder);
var wrongBaseline = WinterLibraryService.CreateBaseline(wrongLib);
Reject(() => { _ = WinterLibraryService.Compare(saved, wrongBaseline); },
    "24 Cross-library comparison blocked");
Reject(() => WinterLibraryService.SaveBaseline(differentFolder, saved, baselineDir),
    "25 Cannot save baseline under wrong library fingerprint");
var outsider = Mod("outside.package", 4, "Z");
outsider.FilePath = Path.Combine(root, "outside.package");
Reject(() => { _ = WinterLibraryService.CreateBaseline(Scan(new List<ModFile> { outsider })); },
    "26 Guardian refuses out-of-library file path");
var duplicateName = Mod("SYNTHETICALPHA.package", 19, "ABC");
Reject(() => { _ = WinterLibraryService.CreateBaseline(Scan(new List<ModFile> { m1, duplicateName })); },
    "27 Guardian refuses duplicate relative paths ignoring case");
var corruptBaselineFile = WinterLibraryService.GetGuardianPath(fakeMods, Path.Combine(privateData, "CorruptGuardian"));
Directory.CreateDirectory(Path.GetDirectoryName(corruptBaselineFile)!);
File.WriteAllText(corruptBaselineFile, "not JSON", Encoding.UTF8);
Reject(() => { _ = WinterLibraryService.LoadBaseline(fakeMods, Path.GetDirectoryName(corruptBaselineFile)!); },
    "28 Corrupted Guardian reference rejected");
Check(InitialContents().Count == original.Count &&
      InitialContents().All(p => original.TryGetValue(p.Key, out string? hash) && p.Value == hash),
    "29 All synthetic Mods fixtures unchanged after operations");
Check(!Directory.GetFiles(privateData, "*.tmp", SearchOption.AllDirectories).Any(),
    "30 No leftover atomic files in private QA sandbox");
Check(Directory.GetFiles(fakeMods, "*", SearchOption.AllDirectories).Length == 3,
    "31 No unexpected files added inside synthetic Mods folder");


// Additional privacy and moderate-scale deterministic regression exercises.
var scaleClock = System.Diagnostics.Stopwatch.StartNew();
var bulkItems = Enumerable.Range(0, 4000).Select(i =>
    Mod($"Batch{i / 100:D3}/Fixture_{i:D5}.package", 10 + i, $"MD5_{i:D5}",
        i % 2 == 0 ? "Hair" : "Scripts & Mods")).ToList();
var bulkScan = Scan(bulkItems);
var bulkCapsule = WinterLibraryService.MakeCapsule(bulkScan);
Check(bulkCapsule.Total == 4000 && bulkCapsule.Packages == 4000,
    "32 A 4000-entry synthetic scan aggregates correctly");
Check(!JsonConvert.SerializeObject(bulkCapsule).Contains("Fixture_") &&
      !JsonConvert.SerializeObject(bulkCapsule).Contains(fakeMods),
    "33 Large capsule excludes names and absolute folder paths");
var bulkBaseline = WinterLibraryService.CreateBaseline(bulkScan);
Check(bulkBaseline.Files.Count == 4000 &&
      bulkBaseline.Files.All(f => !Path.IsPathRooted(f.Relative)),
    "34 Large Guardian reference keeps only relative names");
var bulkDir = Path.Combine(privateData, "StressGuardian");
WinterLibraryService.SaveBaseline(fakeMods, bulkBaseline, bulkDir);
var bulkRestored = WinterLibraryService.LoadBaseline(fakeMods, bulkDir)!;
Check(bulkRestored.Files.Count == 4000 &&
      !File.ReadAllText(WinterLibraryService.GetGuardianPath(fakeMods, bulkDir)).Contains(fakeMods),
    "35 Large reference survives local sandbox persistence without absolute Mods path");
var nextItems = bulkItems.Where(f => !f.FilePath.EndsWith("Fixture_00030.package", StringComparison.OrdinalIgnoreCase)).ToList();
nextItems = nextItems.Select(f =>
    f.FilePath.EndsWith("Fixture_00022.package", StringComparison.OrdinalIgnoreCase)
        ? Mod("Batch000/Fixture_00022.package", f.FileSize, "REHASH_22") : f).ToList();
nextItems.Add(Mod("NewFixtures/NewSynthetic.package", 77, "NEW_SHA"));
var bulkAfter = WinterLibraryService.CreateBaseline(Scan(nextItems));
var bulkDiff = WinterLibraryService.Compare(bulkRestored, bulkAfter);
Check(bulkDiff.Added == 1 && bulkDiff.Changed == 1 && bulkDiff.Missing == 1,
    "36 Large comparison detects exactly one added, modified and missing entry");
Check(bulkDiff.Changes.Count == 3 &&
      bulkDiff.Changes.All(c => !Path.IsPathRooted(c.Relative)),
    "37 Change report uses relative paths only");
Check(!Directory.GetFiles(privateData, "*.tmp", SearchOption.AllDirectories).Any(),
    "38 No residual atomic temporary files after stress persistence");
Check(InitialContents().Count == original.Count &&
      InitialContents().All(p => original.TryGetValue(p.Key, out string? hash) && p.Value == hash),
    "39 Original synthetic fixtures retain identical bytes after stress suite");
scaleClock.Stop();
Console.WriteLine($"METRIC: bulk4000ElapsedMilliseconds={scaleClock.ElapsedMilliseconds}");
// Winter 36 extra stress, still entirely in the disposable QA sandbox.
var megaClock = System.Diagnostics.Stopwatch.StartNew();
var megaItems = Enumerable.Range(0, 12000).Select(i =>
    Mod($"Mega{i / 150:D3}/FixtureMega_{i:D5}.package", 31 + i, $"HASH_{i:D5}",
        i % 3 == 0 ? "Scripts & Mods" : "Hair")).ToList();
var megaScan = Scan(megaItems);
var megaCapsule = WinterLibraryService.MakeCapsule(megaScan);
Check(megaCapsule.Total == 12000 && megaCapsule.Packages == 12000,
    "40 A 12000-entry synthetic capsule aggregates correctly");
var megaJson = JsonConvert.SerializeObject(megaCapsule);
Check(!megaJson.Contains("FixtureMega_") && !megaJson.Contains(fakeMods),
    "41 Stress capsule exports statistical data only");
var megaBaseline = WinterLibraryService.CreateBaseline(megaScan);
Check(megaBaseline.Files.Count == 12000 &&
    megaBaseline.Files.All(x => !Path.IsPathRooted(x.Relative)),
    "42 A 12000-entry Guardian reference remains relative");
var megaDir = Path.Combine(privateData, "MegaGuardian");
WinterLibraryService.SaveBaseline(fakeMods, megaBaseline, megaDir);
var megaSaved = WinterLibraryService.LoadBaseline(fakeMods, megaDir)!;
Check(megaSaved.Files.Count == 12000 &&
    !File.ReadAllText(WinterLibraryService.GetGuardianPath(fakeMods, megaDir)).Contains(fakeMods),
    "43 Stress reference persists without absolute Mods path");
var megaNext = megaItems.Where(x => !x.FilePath.EndsWith("FixtureMega_00120.package", StringComparison.OrdinalIgnoreCase)).ToList();
megaNext = megaNext.Select(x => x.FilePath.EndsWith("FixtureMega_00075.package", StringComparison.OrdinalIgnoreCase)
    ? Mod("Mega000/FixtureMega_00075.package", x.FileSize, "CHANGED_075") : x).ToList();
megaNext.Add(Mod("Extra/NewMega.package", 333, "NEW_MEGA"));
var megaDiff = WinterLibraryService.Compare(megaSaved, WinterLibraryService.CreateBaseline(Scan(megaNext)));
Check(megaDiff.Added == 1 && megaDiff.Changed == 1 && megaDiff.Missing == 1 &&
    megaDiff.Changes.All(x => !Path.IsPathRooted(x.Relative)),
    "44 Stress comparison detects added, modified and missing safely");
Check(!Directory.GetFiles(privateData, "*.tmp", SearchOption.AllDirectories).Any() &&
    InitialContents().Count == original.Count &&
    InitialContents().All(p => original.TryGetValue(p.Key, out string? hash) && p.Value == hash),
    "45 Stress QA left no temp files and preserved all synthetic fixtures");
megaClock.Stop();
Console.WriteLine($"METRIC: bulk12000ElapsedMilliseconds={megaClock.ElapsedMilliseconds}");
Console.WriteLine($"SUCCESS: {checks} Winter 36 functional checks passed on disposable synthetic data.");
