using System.Runtime.InteropServices;
using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace PowerMate.Services;

public class HidService : IHidService
{
    private const int VendorId  = 0x077D;
    private const int ProductId = 0x0410;

    private HidDevice? _device;
    private HidStream? _stream;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _suspended;

    public event Action<int>?  Rotated;           // +1 CW, -1 CCW
    public event Action?       ButtonPressed;
    public event Action?       ButtonReleased;
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    public void Start()
    {
        var thread = new Thread(PollLoop) { IsBackground = true, Name = "PowerMate-HID" };
        thread.Start();
    }

    private void PollLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            if (_suspended) { Thread.Sleep(500); continue; }

            if (!IsConnected)
            {
                TryConnect();
                if (!IsConnected) { Thread.Sleep(2000); continue; }
            }

            try { Read(); }
            catch (TimeoutException) { /* normal — no input within 500 ms */ }
            catch { Disconnect(); }
        }
    }

    public void Suspend()
    {
        _suspended = true;
        Disconnect();
    }

    public void Resume()
    {
        _suspended = false;
        // PollLoop auto-reconnects on next iteration
    }

    private void TryConnect()
    {
        try
        {
            _device = DeviceList.Local.GetHidDeviceOrNull(VendorId, ProductId);
            if (_device == null) return;

            _stream = _device.Open();
            _stream.ReadTimeout = 500;
            IsConnected = true;
            ConnectionChanged?.Invoke(true);
            SetLed(128);
        }
        catch { _device = null; _stream = null; }
    }

    private void Disconnect()
    {
        _stream?.Dispose();
        _stream      = null;
        _device      = null;
        IsConnected  = false;
        ConnectionChanged?.Invoke(false);
    }

    // ── HID read ──────────────────────────────────────────────────────────────
    private byte _lastButton;

    private void Read()
    {
        var buf = new byte[8];
        if (_stream!.Read(buf) < 3) return;

        // buf[1] = button (1=pressed 0=released), buf[2] = rotation (1=CW 255=CCW)
        if (buf[2] == 1)   Rotated?.Invoke(+1);
        if (buf[2] == 255) Rotated?.Invoke(-1);

        if (buf[1] != _lastButton)
        {
            if (buf[1] == 1) ButtonPressed?.Invoke();
            else             ButtonReleased?.Invoke();
            _lastButton = buf[1];
        }
    }

    // ── LED control ───────────────────────────────────────────────────────────
    public void SetLed(byte brightness)
    {
        if (_stream == null) return;
        try
        {
            var report = new byte[2];
            report[0] = 0x00;
            report[1] = brightness;
            _stream.WriteAsync(report);
        }
        catch { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _stream?.Dispose();
    }
}
