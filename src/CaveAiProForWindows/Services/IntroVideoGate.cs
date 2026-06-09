using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>First-run intro gating (see <see cref="Views.IntroVideoWindow"/>).</summary>
public static class IntroVideoGate
{
    public static bool ShouldShowFirstRun(AppUiSettingsModel settings) => !settings.HasSeenIntroVideo;
}
