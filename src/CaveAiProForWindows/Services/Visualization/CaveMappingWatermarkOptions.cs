namespace CaveAiProForWindows.Services.Visualization;

/// <summary>Brand watermark applied to exported map rasters and vectors.</summary>
public sealed class CaveMappingWatermarkOptions
{
    public static CaveMappingWatermarkOptions Default { get; } = new();

    /// <summary>Pack URI to embedded logo PNG (default: app launcher mark).</summary>
    public string LogoPackUri { get; init; } = "pack://application:,,,/Assets/ic_launcher_logo.png";

    /// <summary>Corner placement for raster exports.</summary>
    public CaveMappingWatermarkCorner Corner { get; init; } = CaveMappingWatermarkCorner.BottomRight;

    /// <summary>Logo width as a fraction of the shorter map edge (0.08 … 0.25).</summary>
    public double LogoWidthFraction { get; init; } = 0.14;

    /// <summary>Opacity of the corner logo (0 … 1).</summary>
    public double Opacity { get; init; } = 0.88;

    /// <summary>Margin from canvas edge as fraction of shorter edge.</summary>
    public double MarginFraction { get; init; } = 0.025;

    /// <summary>When true, faint centre watermark (print-style) is also applied on PNG exports.</summary>
    public bool IncludeCentreFaintMark { get; init; } = true;

    public double CentreFaintOpacity { get; init; } = 0.06;
}

public enum CaveMappingWatermarkCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}
