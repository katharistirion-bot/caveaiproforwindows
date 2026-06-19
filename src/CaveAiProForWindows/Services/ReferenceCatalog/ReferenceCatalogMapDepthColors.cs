using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

/// <summary>
/// Cross-platform Public Library map depth tiers — keep in sync with
/// website <c>src/utils/mapDepthColors.js</c> and Android <c>PublicLibraryMapDepthColors.kt</c>.
/// Shallow: 0–50 m · Medium: 50–150 m · Deep: &gt;150 m · Unknown: no depthM
/// </summary>
public static class ReferenceCatalogMapDepthColors
{
    public const double ShallowMaxM = 50;
    public const double MediumMaxM = 150;

    public static readonly MediaColor Shallow = MediaColor.FromRgb(0x15, 0x65, 0xC0);
    public static readonly MediaColor Medium = MediaColor.FromRgb(0xEF, 0x6C, 0x00);
    public static readonly MediaColor Deep = MediaColor.FromRgb(0xC6, 0x28, 0x28);
    public static readonly MediaColor Unknown = MediaColor.FromRgb(0xFF, 0xB7, 0x4D);

    private static readonly Brush ShallowBrush = Freeze(new SolidColorBrush(Shallow));
    private static readonly Brush MediumBrush = Freeze(new SolidColorBrush(Medium));
    private static readonly Brush DeepBrush = Freeze(new SolidColorBrush(Deep));
    private static readonly Brush UnknownBrush = Freeze(new SolidColorBrush(Unknown));

    public static Brush ReferenceFillBrush(double? depthM)
    {
        if (depthM is not > 0)
            return UnknownBrush;
        if (depthM >= MediumMaxM)
            return DeepBrush;
        if (depthM >= ShallowMaxM)
            return MediumBrush;
        return ShallowBrush;
    }

    public static MediaColor ReferenceFillColor(double? depthM) =>
        depthM is not > 0 ? Unknown
        : depthM >= MediumMaxM ? Deep
        : depthM >= ShallowMaxM ? Medium
        : Shallow;

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
