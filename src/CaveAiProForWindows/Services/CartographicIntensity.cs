namespace CaveAiProForWindows.Services;

/// <summary>Controls sketch-style map rendering strength (grid, shadow, traverse dash).</summary>
public enum CartographicIntensity
{
    Subtle,
    Balanced,
    Rich,
}

public static class CartographicIntensityParser
{
    public static CartographicIntensity Parse(string? s) =>
        Enum.TryParse<CartographicIntensity>(s?.Trim(), true, out var v) ? v : CartographicIntensity.Balanced;

    public static string ToPersistedString(CartographicIntensity v) => v.ToString();
}
