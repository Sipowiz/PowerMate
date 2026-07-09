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
    private int _sessionGeneration;

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
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        }

        // Any session swap invalidates a position captured against the old one.
        Interlocked.Increment(ref _sessionGeneration);
        _session = session;

        if (_session == null)
        {
            SetState(PlaybackState.Unknown);
            return;
        }

        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;

        RefreshPlaybackInfo();
        RefreshTimeline();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession s,
        PlaybackInfoChangedEventArgs args) => RefreshPlaybackInfo();

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession s,
        TimelinePropertiesChangedEventArgs args) => RefreshTimeline();

    // The track changed within the same session (auto-advance). Unlike a timeline
    // update, this genuinely invalidates any position captured earlier.
    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession s,
        MediaPropertiesChangedEventArgs args) => Interlocked.Increment(ref _sessionGeneration);

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

    public TimeSpan GetPosition()
    {
        if (_duration <= TimeSpan.Zero) return TimeSpan.Zero;
        var elapsed = _isPlaying ? DateTime.UtcNow - _lastPositionStamp : TimeSpan.Zero;
        var pos = _lastKnownPosition + elapsed;
        return TimeSpan.FromSeconds(Math.Clamp(pos.TotalSeconds, 0, _duration.TotalSeconds));
    }

    public TimeSpan GetDuration() => _duration;

    public int GetSessionGeneration() => Volatile.Read(ref _sessionGeneration);

    public float GetPlaybackPosition()
    {
        if (_duration <= TimeSpan.Zero) return 0f;
        return (float)(GetPosition().TotalSeconds / _duration.TotalSeconds);
    }

    // Stateless and absolute: the caller owns the anchor, so this must not write
    // any position back, or a caller's cached anchor would drift under it.
    public async Task<bool> SeekToAsync(TimeSpan position)
    {
        if (_session == null) return false;
        try { return await _session.TryChangePlaybackPositionAsync(position.Ticks); }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_session != null)
        {
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        }
        if (_manager != null)
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
    }
}
