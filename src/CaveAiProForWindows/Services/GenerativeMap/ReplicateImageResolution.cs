namespace CaveAiProForWindows.Services.GenerativeMap;

/// <summary>
/// Replicate <c>jagilley/controlnet-scribble</c> accepts <c>image_resolution</c> as
/// the string enum values <c>"256"</c>, <c>"512"</c>, or <c>"768"</c> only.
/// </summary>
public static class ReplicateImageResolution
{
    public static readonly int[] AllowedValues = [256, 512, 768];

    /// <summary>
    /// Snaps an arbitrary pixel count to the nearest allowed resolution.
    /// Ties prefer the higher resolution.
    /// </summary>
    public static int Snap(int value)
    {
        var best = AllowedValues[0];
        var bestDistance = Math.Abs(value - best);

        foreach (var candidate in AllowedValues)
        {
            var distance = Math.Abs(value - candidate);
            if (distance < bestDistance || (distance == bestDistance && candidate > best))
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    public static string ToApiString(int value) => Snap(value).ToString();
}
