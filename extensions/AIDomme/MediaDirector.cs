namespace AIDomme;

/// <summary>
/// Decides what the domme "plays" on the media player. It does not pick concrete items — it cues a
/// media <em>kind</em> (video/image/audio) and bumps a token on its own timed cadence (independent of
/// the user's task/step changes), so the frontend pulls a fresh random Cove item of that kind whenever
/// she decides to switch. Holding the selection backend-side keeps media domme-driven (she controls
/// when and what kind) while the browser, which already holds the user's auth, handles fetching and
/// playback.
/// </summary>
public sealed class MediaDirector
{
    // Weighted toward video, with images and audio mixed in for variety.
    private static readonly string[] Kinds = ["video", "video", "video", "image", "image", "audio"];
    private static readonly TimeSpan MinHold = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MaxHold = TimeSpan.FromSeconds(105);

    private readonly object _gate = new();
    private int? _sessionId;
    private int _token;
    private string _kind = "video";
    private DateTime _nextChangeUtc = DateTime.MinValue;

    /// <summary>
    /// Returns the current media cue for the live session, advancing to a fresh kind/token when a new
    /// session starts or when the current item has been held long enough. Returns null when no session
    /// is active. The cadence is time-based, NOT tied to step/directive changes.
    /// </summary>
    public AiDommeMediaCue? Advance(int? sessionId, bool sessionActive)
    {
        lock (_gate)
        {
            if (!sessionActive || sessionId is null)
            {
                _sessionId = null;
                return null;
            }

            var now = DateTime.UtcNow;
            var newSession = sessionId != _sessionId;
            if (newSession || now >= _nextChangeUtc)
            {
                _sessionId = sessionId;
                _token++;
                _kind = Kinds[Random.Shared.Next(Kinds.Length)];
                _nextChangeUtc = now + NextHold();
            }

            return new AiDommeMediaCue(_kind, _token);
        }
    }

    private static TimeSpan NextHold()
        => TimeSpan.FromSeconds(Random.Shared.Next((int)MinHold.TotalSeconds, (int)MaxHold.TotalSeconds + 1));
}
