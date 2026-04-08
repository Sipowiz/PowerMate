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
    void StartBassCapture(int cutoffHz, float gain);
    void StopBassCapture();

    /// <summary>Fired when the system volume or mute state changes from any source.</summary>
    event Action<float, bool>? VolumeChanged;
}
