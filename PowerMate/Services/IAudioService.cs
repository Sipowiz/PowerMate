namespace PowerMate.Services;

public interface IAudioService : IDisposable
{
    float GetLevel();
    void SetLevel(float level);
    void AdjustLevel(float delta);
    bool IsMuted();
    void ToggleMute();
    float GetPeakLevel();
    float GetBassPeak();
    void StartPeakCapture();
    void StartBassCapture(int cutoffHz, float gain);
    void StopCapture();

    /// <summary>True while a loopback capture is running. Goes false on its own when
    /// the render stream dies (source stopped, device switched), so callers can re-arm.</summary>
    bool IsCapturing { get; }

    /// <summary>Fired when the system volume or mute state changes from any source.</summary>
    event Action<float, bool>? VolumeChanged;
}
