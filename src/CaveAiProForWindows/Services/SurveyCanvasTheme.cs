namespace CaveAiProForWindows.Services;

/// <summary>Signals plan/section canvas rendering when the UI palette switches between light and dark.</summary>
public static class SurveyCanvasTheme
{
    public static bool IsDark { get; private set; }

    public static event Action? Changed;

    internal static void SetDark(bool dark)
    {
        if (IsDark == dark)
            return;
        IsDark = dark;
        Changed?.Invoke();
    }
}
