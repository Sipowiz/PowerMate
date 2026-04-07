namespace PowerMate.Services;

public interface IHidService : IDisposable
{
    event Action<int>?  Rotated;
    event Action?       ButtonPressed;
    event Action?       ButtonReleased;
    event Action<bool>? ConnectionChanged;
    bool IsConnected { get; }
    void Start();
    void SetLed(byte brightness);
}
