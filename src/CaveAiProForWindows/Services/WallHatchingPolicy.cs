namespace CaveAiProForWindows.Services;

/// <summary>When to draw diagonal rock hatching inside LRUD passage fills.</summary>
public static class WallHatchingPolicy
{
    public static bool ShouldEnable(PlanCanvasDrawOptions opt) => opt.ShowWallHatching;
}
