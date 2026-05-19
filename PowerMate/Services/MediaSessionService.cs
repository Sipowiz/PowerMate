using Windows.Media.Control;
using SmtcStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace PowerMate.Services;

public class MediaSessionService : IMediaSessionService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    private PlaybackState _state = PlaybackState.Unknown;
    private bool _isPlaying;
    private TimeSpan _lastKnownPosition;
    private TimeSpan _duration;
    private DateTime _lastPositionStamp;

    public event Action<PlaybackState>? PlaybackStateChanged;

    public MediaSessionService()
    {
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            AttachSession(_manager.GetCurrentSession());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SMTC] init failed: {ex.Message}");
        }
    }

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        AttachSession(sender.GetCurrentSession());
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session != null)
        {
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        _session = session;

        if (_session == null)
        {
            SetState(PlaybackState.Unknown);
            return;
        }

        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

        RefreshPlaybackInfo();
        RefreshTimeline();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession s,
        PlaybackInfoChangedEventArgs args) => RefreshPlaybackInfo();

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession s,
        TimelinePropertiesChangedEventArgs args) => RefreshTimeline();

    private void RefreshPlaybackInfo()
    {
        try
        {
            var status = _session?.GetPlaybackInfo()?.PlaybackStatus;
            var newState = status switch
            {
                SmtcStatus.Playing  => PlaybackState.Playing,
                SmtcStatus.Paused   => PlaybackState.Paused,
                SmtcStatus.Stopped  => PlaybackState.Stopped,
                _                   => PlaybackState.Unknown,
            };
            _isPlaying = newState == PlaybackState.Playing;
            SetState(newState);
        }
        catch { }
    }

    // GetTimelineProperties() is synchronous in the SMTC API
    private void RefreshTimeline()
    {
        try
        {
            if (_session == null) return;
            var tl = _session.GetTimelineProperties();
            _lastKnownPosition = tl.Position;
            _lastPositionStamp = DateTime.UtcNow;
            _duration          = tl.EndTime - tl.StartTime;
        }
        catch { }
    }

    private void SetState(PlaybackState s)
    {
        if (_state == s) return;
        _state = s;
        PlaybackStateChanged?.Invoke(s);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public PlaybackState GetPlaybackState() => _state;

    public float GetPlaybackPosition()
    {
        if (_duration <= TimeSpan.Zero) return 0f;
        var elapsed = _isPlaying ? DateTime.UtcNow - _lastPositionStamp : TimeSpan.Zero;
        var pos = _lastKnownPosition + elapsed;
        pos = TimeSpan.FromSeconds(Math.Clamp(pos.TotalSeconds, 0, _duration.TotalSeconds));
        return (float)(pos.TotalSeconds / _duration.TotalSeconds);
    }

    public async Task SeekRelativeAsync(TimeSpan delta)
    {
        if (_session == null) return;
        try
        {
            var tl = _session.GetTimelineProperties();

            var newPos = tl.Position + delta;
            newPos = TimeSpan.FromSeconds(
                Math.Clamp(newPos.TotalSeconds,
                    tl.StartTime.TotalSeconds,
                    tl.EndTime.TotalSeconds));

            await _session.TryChangePlaybackPositionAsync(
                (long)(newPos.TotalSeconds * 10_000_000)); // 100-ns ticks

            _lastKnownPosition = newPos;
            _lastPositionStamp = DateTime.UtcNow;
        }
        catch { }
    }

    public void Dispose()
    {
        if (_session != null)
        {
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }
        if (_manager != null)
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
    }
}
