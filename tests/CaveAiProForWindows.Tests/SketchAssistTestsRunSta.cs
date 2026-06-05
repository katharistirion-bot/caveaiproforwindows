namespace CaveAiProForWindows.Tests;

/// <summary>Shared STA runner for WPF imaging tests.</summary>
internal static class SketchAssistTestsRunSta
{
    public static void Run(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        if (caught != null)
            throw caught;
    }
}
