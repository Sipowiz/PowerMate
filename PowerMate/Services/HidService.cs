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
    private string?    _devicePath;
    private readonly CancellationTokenSource _cts = new();

    public event Action<int>?  Rotated;           // +1 CW, -1 CCW
    public event Action?       ButtonPressed;
    public event Action?       ButtonReleased;
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected { get; private set; }

    // ── Win32 P/Invoke ────────────────────────────────────────────────────────
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(
        SafeFileHandle hDevice, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetOutputReport(
        SafeFileHandle hDevice, byte[] reportBuffer, int reportBufferLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        SafeFileHandle hFile, byte[] buffer, int bytesToWrite,
        out int bytesWritten, IntPtr overlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    private const uint GENERIC_WRITE   = 0x40000000;
    private const uint FILE_SHARE_READ  = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING    = 3;

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

    private void TryConnect()
    {
        try
        {
            _device = DeviceList.Local.GetHidDeviceOrNull(VendorId, ProductId);
            if (_device == null) return;

            _devicePath = _device.DevicePath;
            _stream = _device.Open();
            _stream.ReadTimeout = 500;
            IsConnected = true;
            ConnectionChanged?.Invoke(true);
            SetLed(128);
        }
        catch { _device = null; _stream = null; _devicePath = null; }
    }

    private void Disconnect()
    {
        _stream?.Dispose();
        _stream      = null;
        _device      = null;
        _devicePath  = null;
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
        if (_devicePath == null) return;
        try
        {
            using var handle = CreateFile(
                _devicePath,
                GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid) return;

            var buf = new byte[9];
            buf[0] = 0x00;
            buf[1] = brightness;

            // Try all three; whichever succeeds on this firmware wins.
            if (!HidD_SetFeature(handle, buf, buf.Length))
                if (!HidD_SetOutputReport(handle, buf, buf.Length))
                    WriteFile(handle, buf, buf.Length, out _, IntPtr.Zero);
        }
        catch { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _stream?.Dispose();
    }
}
