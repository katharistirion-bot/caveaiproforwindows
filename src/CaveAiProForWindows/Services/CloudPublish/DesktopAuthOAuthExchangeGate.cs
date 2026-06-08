namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Ensures each OAuth authorization code / id_token is exchanged at most once per app session.
/// </summary>
public sealed class DesktopAuthOAuthExchangeGate
{
    private readonly HashSet<string> _completedKeys = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _inFlight;

    /// <summary>Returns false if this callback was already consumed or another exchange is running.</summary>
    public bool TryBeginExchange(string dedupeKey)
    {
        lock (_gate)
        {
            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
                return false;

            if (!_completedKeys.Add(dedupeKey))
            {
                Interlocked.Exchange(ref _inFlight, 0);
                return false;
            }

            return true;
        }
    }

    public void EndExchange()
    {
        Interlocked.Exchange(ref _inFlight, 0);
    }
}
