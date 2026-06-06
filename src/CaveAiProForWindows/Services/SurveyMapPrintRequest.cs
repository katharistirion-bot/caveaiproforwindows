using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Captured map bitmap plus title plate metadata for <see cref="PrintLayoutService"/>.</summary>
public sealed class SurveyMapPrintRequest
{
    public required CaveProjectDocument Project { get; init; }

    public required SurveyMapPrintKind Kind { get; init; }

    public required BitmapSource MapImage { get; init; }

    public string? SubtitleSuffix { get; init; }

    public bool HighContrast { get; init; }

    public SurveyMapPrintContext? Cartography { get; init; }

    public string MapKindLabel => Kind switch
    {
        SurveyMapPrintKind.Plan => "Plan view (survey metres)",
        SurveyMapPrintKind.Section => "Section view (survey metres)",
        SurveyMapPrintKind.XRay => "X-Ray (geo-aligned satellite overlay)",
        SurveyMapPrintKind.Plan3D => "3D model (LRUD tube)",
        _ => "Survey map",
    };

    public string JobName =>
        $"CAVE AI PRO — {Project.Name} {Kind}";
}
