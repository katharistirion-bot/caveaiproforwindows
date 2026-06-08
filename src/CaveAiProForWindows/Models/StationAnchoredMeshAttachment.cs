using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace CaveAiProForWindows.Models;

/// <summary>External triangle mesh anchored to a survey station in local metres.</summary>
public sealed class StationAnchoredMeshAttachment
{
    public required string Id { get; init; }

    public required string AnchorStationName { get; init; }

    /// <summary>Local file path or ZIP-relative URI to OBJ/STL source.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Translation offset from anchor station (metres, survey frame).</summary>
    public float OffsetX { get; init; }

    public float OffsetY { get; init; }

    public float OffsetZ { get; init; }

    /// <summary>Yaw about Z (degrees).</summary>
    public float RotationZDeg { get; init; }

    /// <summary>Uniform scale applied after unit normalization.</summary>
    public float Scale { get; init; } = 1f;

    public string? DisplayName { get; init; }
}

/// <summary>Parsed mesh ready for WPF <see cref="MeshGeometry3D"/>.</summary>
public sealed class ParsedTriangleMesh
{
    public required Point3DCollection Positions { get; init; }

    public required Int32Collection TriangleIndices { get; init; }

    public Point3D BoundsMin { get; init; }

    public Point3D BoundsMax { get; init; }

    public MeshGeometry3D ToWpfMesh() =>
        new() { Positions = Positions, TriangleIndices = TriangleIndices };
}
