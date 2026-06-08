namespace CaveAiProForWindows.Services;

/// <summary>Screen-space scale for 3D viewport label chips (Small / Medium / Large).</summary>
public static class Viewport3DLabelSizeScale
{
    public const string Small = "Small";
    public const string Medium = "Medium";
    public const string Large = "Large";

    public static double Factor(string? size) => size switch
    {
        Small => 0.82,
        Large => 1.22,
        _ => 1.0,
    };
}
