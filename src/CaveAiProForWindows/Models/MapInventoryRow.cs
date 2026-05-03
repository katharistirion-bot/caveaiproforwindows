namespace CaveAiProForWindows.Models;

/// <summary>One row from Android <c>map_inventory.json</c> inside a backup ZIP (cartography slots per cave).</summary>
public sealed class MapInventoryRow
{
    public MapInventoryRow(string sourceZip, string projectName, string projectDate, string slot, string pathInBackupOrUrl, string storageKind)
    {
        SourceZip = sourceZip;
        ProjectName = projectName;
        ProjectDate = projectDate;
        Slot = slot;
        PathInBackupOrUrl = pathInBackupOrUrl;
        StorageKind = storageKind;
    }

    public string SourceZip { get; }
    public string ProjectName { get; }
    public string ProjectDate { get; }
    public string Slot { get; }
    public string PathInBackupOrUrl { get; }
    public string StorageKind { get; }
}
