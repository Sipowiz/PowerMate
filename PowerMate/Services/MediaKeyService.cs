using System.Runtime.InteropServices;

namespace PowerMate.Services;

public static class MediaKeyService
{
    private const byte VK_MEDIA_PLAY_PAUSE  = 0xB3;
    private const byte VK_MEDIA_NEXT_TRACK  = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK  = 0xB1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    private static void Press(byte vk)
    {
        keybd_event(vk, 0, 0, 0);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
    }

    public static void PlayPause()    => Press(VK_MEDIA_PLAY_PAUSE);
    public static void NextTrack()    => Press(VK_MEDIA_NEXT_TRACK);
    public static void PreviousTrack() => Press(VK_MEDIA_PREV_TRACK);
}
