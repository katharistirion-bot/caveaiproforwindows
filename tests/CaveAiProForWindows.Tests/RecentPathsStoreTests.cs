using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class RecentPathsStoreTests
{
    [TestMethod]
    public void Remove_drops_path_from_store_without_touching_disk_file()
    {
        var dir = CreateTempStoreDir(out var storePath);
        var keep = Path.Combine(dir, "keep.zip");
        var drop = Path.Combine(dir, "drop.zip");
        File.WriteAllText(keep, "x");
        File.WriteAllText(drop, "x");
        WriteStore(storePath, drop, keep);

        WithStore(storePath, () =>
        {
            RecentPathsStore.Remove(drop);
            var loaded = RecentPathsStore.Load();
            CollectionAssert.DoesNotContain(loaded.ToList(), drop);
            CollectionAssert.Contains(loaded.ToList(), keep);
            Assert.IsTrue(File.Exists(drop), "Remove must not delete the file on disk.");
        });
    }

    [TestMethod]
    public void ReplacePath_updates_entry_after_rename_on_disk()
    {
        var dir = CreateTempStoreDir(out var storePath);
        var oldPath = Path.Combine(dir, "old.zip");
        var newPath = Path.Combine(dir, "new.zip");
        File.WriteAllText(oldPath, "x");
        WriteStore(storePath, oldPath);

        WithStore(storePath, () =>
        {
            Assert.IsTrue(RecentPathFileOps.TryRename(oldPath, "new.zip", out var renamed, out _));
            Assert.AreEqual(newPath, renamed);
            RecentPathsStore.ReplacePath(oldPath, newPath!);
            var loaded = RecentPathsStore.Load();
            CollectionAssert.Contains(loaded.ToList(), newPath);
            CollectionAssert.DoesNotContain(loaded.ToList(), oldPath);
        });
    }

    private static string CreateTempStoreDir(out string storePath)
    {
        var dir = Path.Combine(Path.GetTempPath(), "CaveAiRecentTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        storePath = Path.Combine(dir, "recent.json");
        return dir;
    }

    private static void WriteStore(string storePath, params string[] paths)
    {
        var jsonPaths = string.Join(",\n    ", paths.Select(p => $"\"{p.Replace("\\", "\\\\")}\""));
        File.WriteAllText(storePath, $$"""
            {
              "Paths": [
                {{jsonPaths}}
              ]
            }
            """);
    }

    private static void WithStore(string storePath, Action action)
    {
        var dir = Path.GetDirectoryName(storePath)!;
        Environment.SetEnvironmentVariable("CAVEAI_RECENT_PATH_OVERRIDE", storePath);
        try
        {
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("CAVEAI_RECENT_PATH_OVERRIDE", null);
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
