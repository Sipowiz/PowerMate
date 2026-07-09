namespace PowerMate.Services;

public enum PlaybackState { Unknown, Playing, Paused, Stopped, SkipNext, SkipPrev }

public interface IMediaSessionService : IDisposable
{
    PlaybackState GetPlaybackState();
    float GetPlaybackPosition();        // 0–1, estimated from last known timeline

    /// <summary>Absolute seek. Returns false when the session cannot seek; never throws.</summary>
    Task<bool> SeekToAsync(TimeSpan position);

    TimeSpan GetPosition();             // best-estimate absolute position
    TimeSpan GetDuration();             // TimeSpan.Zero when unknown

    /// <summary>
    /// Bumps whenever the session or the track changes, so a caller holding a
    /// position captured earlier can tell it has gone stale. Deliberately does
    /// NOT bump on timeline updates, which SMTC fires constantly — including
    /// once for every seek we issue.
    /// </summary>
    int GetSessionGeneration();

    event Action<PlaybackState>? PlaybackStateChanged;
}
