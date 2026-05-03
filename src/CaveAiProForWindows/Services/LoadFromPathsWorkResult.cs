using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Immutable outcome of background work for <see cref="LoadFromPathsWorker"/>.</summary>
public sealed class LoadFromPathsWorkResult
{
    public List<CaveProjectDocument> Merged { get; init; } = new();

    public List<KnownCaveRecord> LibraryRecords { get; init; } = new();

    public List<string> LoadedPaths { get; init; } = new();

    public string BackupDataAnalyticsText { get; init; } = "";

    public List<MapInventoryRow> MapInventoryRows { get; init; } = new();

    /// <summary>Single opened .zip path when exactly one zip was loaded — drives integrity UI and primary archive panel.</summary>
    public string? IntegrityZipPath { get; init; }

    public string? AuxiliaryZipForMaps { get; init; }

    public IntegrityReport? IntegrityReport { get; init; }
}
