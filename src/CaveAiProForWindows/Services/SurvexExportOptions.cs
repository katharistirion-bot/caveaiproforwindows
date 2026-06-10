namespace CaveAiProForWindows.Services;

/// <summary>User-selected Survex header options (fix anchor, declination, splays).</summary>
public sealed class SurvexExportOptions
{
    /// <summary>Station name for the <c>*fix</c> directive (sanitized on export).</summary>
    public string FixStation { get; init; } = "";

    public double FixEasting { get; init; }

    public double FixNorthing { get; init; }

    public double FixElevation { get; init; }

    /// <summary>Magnetic declination in degrees; omitted from export when null.</summary>
    public double? DeclinationDeg { get; init; }

    public bool IncludeSplays { get; init; } = true;

    /// <summary>When true, writes a comment that the fix is provisional (0,0,0 or entrance-only).</summary>
    public bool ProvisionalFix { get; init; } = true;

    /// <summary>Emit Survex <c>*data passage</c> LRUD wall dimensions along the traverse.</summary>
    public bool IncludeLrudPassage { get; init; } = true;

    /// <summary>When entrance lat/lon are present, emit a documented <c>*cs long-lat</c> GPS anchor block.</summary>
    public bool IncludeEntranceGpsCs { get; init; } = true;
}
