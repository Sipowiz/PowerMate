namespace PowerMate.Services;

public enum PlaybackState { Unknown, Playing, Paused, Stopped, SkipNext, SkipPrev }

public interface IMediaSessionService : IDisposable
{
    PlaybackState GetPlaybackState();
    float GetPlaybackPosition();        // 0–1, estimated from last known timeline
    Task SeekRelativeAsync(TimeSpan delta);
    event Action<PlaybackState>? PlaybackStateChanged;
    internal void RegisterHandler(Action action);
    public void OnInteractionModeChanged(InteractionMode interactionMode);
}
