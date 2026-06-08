namespace CaveAiProForWindows;

/// <summary>
/// Lets the sign-in gate dismiss the startup splash as soon as auth UI is shown or subscription is denied.
/// </summary>
public static class AppStartupSplashController
{
    private static SplashScreen? _active;

    public static void Register(SplashScreen splash) => _active = splash;

    public static void Clear() => _active = null;

    /// <summary>Stop spinner and hide splash so LoginWindow is the only visible startup UI.</summary>
    public static void DismissForAuthentication()
    {
        var splash = _active;
        if (splash == null)
            return;

        void Dismiss()
        {
            splash.StopProgress();
            splash.Hide();
        }

        if (splash.Dispatcher.CheckAccess())
            Dismiss();
        else
            splash.Dispatcher.Invoke(Dismiss);
    }
}
