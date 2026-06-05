using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AndroidBackupImageDiscoveryTests
{
    private string _zipPath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _zipPath = Path.Combine(Path.GetTempPath(), $"caveai-test-{Guid.NewGuid():N}.zip");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (File.Exists(_zipPath))
                File.Delete(_zipPath);
        }
        catch
        {
            // Test cleanup best-effort.
        }
    }

    /// <summary>
    /// Reproduces the bug scenario: Android writes the X-Ray snapshot to a dynamic per-map folder
    /// (<c>export_assets/maps/Cave__hash/000_satellite_xray_xxxxxxxx/image_09019f.png</c>) and references it via
    /// a <c>map_inventory.json</c> slot. Old Windows code missed it (no <c>/satellite/</c> in path); new code finds it.
    /// </summary>
    [TestMethod]
    public void Discovery_finds_dynamically_named_xray_image_via_map_inventory()
    {
        var entryPath = "export_assets/maps/MyCave__abc1234567/000_satellite_xray_deadbeef/image_09019f.png";
        WriteZip(new Dictionary<string, byte[]>
        {
            [entryPath] = MakePngBytes(),
            ["data.json"] = Encoding.UTF8.GetBytes("[]"),
        });

        var project = new CaveProjectDocument { Name = "MyCave", Date = "2026-05-07" };
        var inventory = new List<MapInventoryRow>
        {
            new(sourceZip: Path.GetFileName(_zipPath),
                projectName: "MyCave",
                projectDate: "2026-05-07",
                slot: "satellite_xray",
                pathInBackupOrUrl: entryPath,
                storageKind: "zip"),
        };

        var hints = AndroidBackupImageDiscovery
            .Enumerate(AndroidBackupImageCategory.XRayBackdrop, project, _zipPath, inventory)
            .ToList();

        Assert.IsTrue(hints.Count > 0, "Expected at least one X-Ray hint.");
        Assert.IsTrue(hints.Any(h => h.RawValue == entryPath),
            $"Expected hint to point at '{entryPath}'. Got: {string.Join(", ", hints.Select(h => h.RawValue))}");
    }

    /// <summary>
    /// Reproduces the bug for the JSON-only path: Android puts the satellite image under a non-standard
    /// extension key (e.g. <c>caveAiSatelliteImageUri</c>) — the broader keyword match must catch it.
    /// </summary>
    [TestMethod]
    public void Discovery_finds_xray_image_via_extension_data_keyword()
    {
        var entryPath = "photos/image_09019f.png";
        WriteZip(new Dictionary<string, byte[]>
        {
            [entryPath] = MakePngBytes(),
            ["data.json"] = Encoding.UTF8.GetBytes("[]"),
        });

        var project = new CaveProjectDocument
        {
            Name = "C",
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["caveAiSatelliteImageUri"] = JsonDocument.Parse($"\"{entryPath}\"").RootElement,
            },
        };

        var hints = AndroidBackupImageDiscovery
            .Enumerate(AndroidBackupImageCategory.XRayBackdrop, project, _zipPath, mapInventory: null)
            .ToList();

        Assert.IsTrue(hints.Any(h => h.RawValue == entryPath),
            $"Expected hint pointing at '{entryPath}'. Got: {string.Join(", ", hints.Select(h => h.RawValue))}");

        var resolved = AndroidBackupImageDiscovery.TryResolveLocalFile(
            hints.First(h => h.RawValue == entryPath), project, _zipPath);
        Assert.IsNotNull(resolved, "TryResolveLocalFile should extract the entry to a temp file.");
        Assert.IsTrue(File.Exists(resolved!), $"Resolved local path should exist: {resolved}");
    }

    /// <summary>
    /// Explicit <see cref="CaveProjectDocument.XrayBackdropImageUri"/> short-circuits before heuristics run,
    /// so a deterministic Android export always works.
    /// </summary>
    [TestMethod]
    public void Discovery_uses_explicit_xray_property_first()
    {
        var entryPath = "export_assets/maps/Cave__abc/000_xray_aaaaaaaa/anything.jpg";
        WriteZip(new Dictionary<string, byte[]>
        {
            [entryPath] = MakeJpegBytes(),
        });

        var project = new CaveProjectDocument
        {
            Name = "Cave",
            XrayBackdropImageUri = entryPath,
        };

        var first = AndroidBackupImageDiscovery
            .Enumerate(AndroidBackupImageCategory.XRayBackdrop, project, _zipPath, mapInventory: null)
            .First();

        Assert.AreEqual(entryPath, first.RawValue);
        Assert.AreEqual("xrayBackdropImageUri", first.SourceLabel);
    }

    /// <summary>
    /// Geology photos under <c>photos/image_*.png</c> with no JSON reference are still discovered via
    /// the broadened ZIP scan (Android's typical photo bucket).
    /// </summary>
    [TestMethod]
    public void Discovery_finds_geology_photo_under_photos_folder()
    {
        var entryPath = "photos/image_09019f.png";
        WriteZip(new Dictionary<string, byte[]>
        {
            [entryPath] = MakePngBytes(),
        });

        var project = new CaveProjectDocument { Name = "C" };

        var hints = AndroidBackupImageDiscovery
            .Enumerate(AndroidBackupImageCategory.GeologyPhoto, project, _zipPath, mapInventory: null)
            .ToList();

        Assert.IsTrue(hints.Any(h => h.RawValue == entryPath),
            $"Expected ZIP scan to surface '{entryPath}'. Got: {string.Join(", ", hints.Select(h => h.RawValue))}");
    }

    /// <summary>
    /// Geology photos referenced from <c>rocks[].imageUri</c> (Android's strong-source field) are surfaced
    /// even when they live at non-rocks/ folders inside the ZIP — leaf-name fallback inside MapAssetOpener
    /// resolves dynamically named files.
    /// </summary>
    [TestMethod]
    public void Discovery_finds_geology_photo_via_rocks_imageUri()
    {
        var entryPath = "photos/image_dead42.png";
        WriteZip(new Dictionary<string, byte[]>
        {
            [entryPath] = MakePngBytes(),
        });

        var project = new CaveProjectDocument
        {
            Name = "C",
            Rocks = JsonDocument
                .Parse($$"""[{ "imageUri": "{{entryPath}}" }]""")
                .RootElement,
        };

        var hint = AndroidBackupImageDiscovery
            .Enumerate(AndroidBackupImageCategory.GeologyPhoto, project, _zipPath, mapInventory: null)
            .FirstOrDefault(h => h.RawValue == entryPath);

        Assert.IsNotNull(hint, "rocks[0].imageUri should be enumerated.");
        var resolved = AndroidBackupImageDiscovery.TryResolveLocalFile(hint!, project, _zipPath);
        Assert.IsNotNull(resolved);
        Assert.IsTrue(File.Exists(resolved!));
    }

    /// <summary>
    /// Cave AI analysis text on the <see cref="CaveProjectDocument.CaveAiGeologyAnalysisText"/> property is
    /// returned directly, ahead of any extension-data text-soup search.
    /// </summary>
    [TestMethod]
    public void FindGeologyAnalysisText_uses_explicit_property()
    {
        var project = new CaveProjectDocument
        {
            Name = "C",
            CaveAiGeologyAnalysisText = "Karst limestone with significant secondary calcite deposition.",
        };

        var text = AndroidBackupImageDiscovery.FindGeologyAnalysisText(project, zipPath: null);
        Assert.IsNotNull(text);
        StringAssert.Contains(text!, "Karst limestone");
    }

    /// <summary>
    /// When the explicit property is empty, the deeper <see cref="CaveProjectDocument.ExtensionData"/> walk
    /// must still find analysis text under broader keyword keys (e.g. <c>aiAnalysisText</c>).
    /// </summary>
    [TestMethod]
    public void FindGeologyAnalysisText_falls_back_to_extension_data_with_broader_keywords()
    {
        var project = new CaveProjectDocument
        {
            Name = "C",
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["aiAnalysisText"] = JsonDocument.Parse(
                        "\"Volcanic basalt outcrop, weathered. Iron oxide staining present.\"")
                    .RootElement,
            },
        };

        var text = AndroidBackupImageDiscovery.FindGeologyAnalysisText(project, zipPath: null);
        Assert.IsNotNull(text);
        StringAssert.Contains(text!, "basalt");
    }

    /// <summary>
    /// Diagnostics: callers can show "ZIP has N images but none matched" — verify count is non-zero when
    /// the ZIP holds rasters at random non-category paths.
    /// </summary>
    [TestMethod]
    public void CountRasterImageEntries_counts_images_regardless_of_path()
    {
        WriteZip(new Dictionary<string, byte[]>
        {
            ["photos/a.png"] = MakePngBytes(),
            ["nested/folder/b.jpg"] = MakeJpegBytes(),
            ["data.json"] = Encoding.UTF8.GetBytes("[]"),
        });

        var n = AndroidBackupImageDiscovery.CountRasterImageEntries(_zipPath);
        Assert.AreEqual(2, n);
    }

    private void WriteZip(IReadOnlyDictionary<string, byte[]> entries)
    {
        if (File.Exists(_zipPath))
            File.Delete(_zipPath);
        using var fs = File.Create(_zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);
        foreach (var (name, bytes) in entries)
        {
            var entry = zip.CreateEntry(name);
            using var es = entry.Open();
            es.Write(bytes, 0, bytes.Length);
        }
    }

    /// <summary>
    /// Tiny but valid 1x1 PNG payload (decodable by WPF/WIC) — used when a test resolves the file and decodes it.
    /// </summary>
    private static byte[] MakePngBytes() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP8z8DwHwAFAQH/" +
        "++JuNwAAAABJRU5ErkJggg==");

    /// <summary>
    /// Placeholder JPEG bytes — discovery only inspects the entry path/extension, so any non-empty payload is fine
    /// for the path-enumeration tests. Tests that decode actually use <see cref="MakePngBytes"/>.
    /// </summary>
    private static byte[] MakeJpegBytes() => Encoding.ASCII.GetBytes("not-a-real-jpeg-but-not-needed-for-path-checks");
}
