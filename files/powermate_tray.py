"""
Griffin PowerMate - System Tray (Python)
Handles HID input, volume control, system tray.
Settings UI is a .NET MAUI app launched as a subprocess.
"""

import pywinusb.hid as hid_lib
import queue
import json
import os
import sys
import time
import threading
import ctypes
import winreg
import subprocess
from PIL import Image, ImageDraw
import pystray
from pystray import MenuItem as item
from comtypes import CLSCTX_ALL
from ctypes import cast, POINTER
from pycaw.pycaw import AudioUtilities, IAudioEndpointVolume

# ── Paths ──────────────────────────────────────────────────────────────────────
BASE_DIR     = os.path.dirname(os.path.abspath(__file__))
CONFIG_FILE  = os.path.join(BASE_DIR, "powermate_config.json")
STATUS_FILE  = os.path.join(BASE_DIR, "powermate_status.json")
SETTINGS_EXE = os.path.join(BASE_DIR, "PowerMateSettings", "PowerMateSettings.exe")
REG_KEY      = r"Software\Microsoft\Windows\CurrentVersion\Run"
REG_VALUE    = "GriffinPowerMate"

POWERMATE_VENDOR_ID  = 0x077D
POWERMATE_PRODUCT_ID = 0x0410

VK_MEDIA_PLAY_PAUSE = 0xB3
VK_MEDIA_STOP       = 0xB2
VK_MEDIA_NEXT_TRACK = 0xB0
VK_MEDIA_PREV_TRACK = 0xB1

DEFAULT_CONFIG = {
    "volume_step":         2,
    "sensitivity":         1,
    "invert_rotation":     False,
    "click_action":        "mute",
    "long_press_action":   "none",
    "long_press_ms":       800,
    "led_brightness":      255,
    "led_pulse_on_volume": False,
    "notifications":       True,
}

# ── Config ─────────────────────────────────────────────────────────────────────
def load_config():
    if os.path.exists(CONFIG_FILE):
        try:
            with open(CONFIG_FILE) as f:
                cfg = json.load(f)
            for k, v in DEFAULT_CONFIG.items():
                cfg.setdefault(k, v)
            return cfg
        except Exception:
            pass
    return DEFAULT_CONFIG.copy()

def write_status(volume, muted):
    try:
        with open(STATUS_FILE, "w") as f:
            json.dump({"volume": volume, "muted": muted}, f)
    except Exception:
        pass

# ── Startup ────────────────────────────────────────────────────────────────────
def set_startup(enabled):
    cmd = f'"{sys.executable}" "{os.path.abspath(__file__)}"'
    try:
        key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, REG_KEY, 0, winreg.KEY_SET_VALUE)
        if enabled:
            winreg.SetValueEx(key, REG_VALUE, 0, winreg.REG_SZ, cmd)
        else:
            try: winreg.DeleteValue(key, REG_VALUE)
            except FileNotFoundError: pass
        winreg.CloseKey(key)
    except Exception:
        pass

def get_startup():
    try:
        key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, REG_KEY, 0, winreg.KEY_READ)
        winreg.QueryValueEx(key, REG_VALUE)
        winreg.CloseKey(key)
        return True
    except Exception:
        return False

# ── Volume ─────────────────────────────────────────────────────────────────────
class VolumeController:
    def __init__(self):
        self._init()

    def _init(self):
        devices = AudioUtilities.GetSpeakers()
        if hasattr(devices, 'Activate'):
            interface = devices.Activate(IAudioEndpointVolume._iid_, CLSCTX_ALL, None)
            self.vol  = cast(interface, POINTER(IAudioEndpointVolume))
        else:
            self.vol = devices.EndpointVolume

    def get(self):
        try: return round(self.vol.GetMasterVolumeLevelScalar() * 100)
        except Exception: self._init(); return 50

    def set(self, pct):
        try: self.vol.SetMasterVolumeLevelScalar(max(0, min(100, pct)) / 100.0, None)
        except Exception: self._init()

    def adjust(self, delta): self.set(self.get() + delta)

    def mute_toggle(self):
        try: self.vol.SetMute(not self.vol.GetMute(), None)
        except Exception: self._init()

    def is_muted(self):
        try: return bool(self.vol.GetMute())
        except Exception: return False

def media_key(vk):
    KEYEVENTF_KEYUP = 0x0002
    ctypes.windll.user32.keybd_event(vk, 0, 0, 0)
    ctypes.windll.user32.keybd_event(vk, 0, KEYEVENTF_KEYUP, 0)

# ── Tray icon ──────────────────────────────────────────────────────────────────
def make_tray_icon(muted=False, volume=50):
    size = 64
    img  = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d    = ImageDraw.Draw(img)
    ring = (80, 80, 80, 255) if muted else (220, 220, 220, 255)
    d.ellipse([2, 2, size-3, size-3], outline=ring, width=4)
    if not muted and volume > 0:
        d.arc([6, 6, size-7, size-7], start=-90, end=-90+int(volume*3.6),
              fill=(100, 200, 255, 255), width=6)
    cx, cy, r = size//2, size//2, 8
    d.ellipse([cx-r, cy-r, cx+r, cy+r],
              fill=(60,60,60,200) if muted else (255,255,255,230))
    if muted:
        d.line([cx-5,cy-5,cx+5,cy+5], fill=(220,60,60,255), width=3)
        d.line([cx+5,cy-5,cx-5,cy+5], fill=(220,60,60,255), width=3)
    return img

# ── LED ────────────────────────────────────────────────────────────────────────
def set_led(device, brightness):
    try:
        handle = None
        for attr in ('hid_handle', '_HidDevice__handle', 'device_handle', '_handle'):
            if hasattr(device, attr):
                handle = getattr(device, attr)
                break
        if handle is None:
            return
        buf    = (ctypes.c_ubyte * 9)()
        buf[0] = 0x00
        buf[1] = 0x41
        buf[4] = int(brightness) & 0xFF
        ctypes.windll.hid.HidD_SetFeature(handle, ctypes.byref(buf), ctypes.sizeof(buf))
    except Exception:
        pass

# ── PowerMate HID Driver ───────────────────────────────────────────────────────
class PowerMateDriver:
    def __init__(self, app):
        self.app         = app
        self._device     = None
        self._press_time = None
        self._long_fired = False
        self._queue      = queue.Queue()

    def _find_device(self):
        f = hid_lib.HidDeviceFilter(
            vendor_id=POWERMATE_VENDOR_ID,
            product_id=POWERMATE_PRODUCT_ID)
        devices = f.get_devices()
        return devices[0] if devices else None

    def _on_data(self, data):
        if data:
            self._queue.put(data[:])

    def _process(self, data):
        if not data or len(data) < 3:
            return
        app     = self.app
        button  = data[1] & 0x01
        raw_rot = data[2]
        rot     = 0 if raw_rot == 0 else (raw_rot if raw_rot < 128 else raw_rot - 256)

        if button == 1 and self._press_time is None:
            self._press_time = time.time()
            self._long_fired = False

        if button == 0 and self._press_time is not None:
            if not self._long_fired:
                app.handle_click()
            self._press_time = None

        if rot != 0:
            app.handle_rotation(self._device, 1 if rot > 0 else -1)

    def run(self):
        app = self.app
        while app.running:
            dev = self._find_device()
            if dev is None:
                app.device_connected = False
                app.update_tray()
                time.sleep(2)
                continue
            try:
                dev.open()
                self._device = dev
                app.device_connected = True
                app.update_tray()
                set_led(dev, app.cfg["led_brightness"])
                dev.set_raw_data_handler(self._on_data)

                while app.running and dev.is_plugged():
                    while True:
                        try:
                            self._process(self._queue.get_nowait())
                        except queue.Empty:
                            break
                    if self._press_time and not self._long_fired:
                        if (time.time() - self._press_time)*1000 >= app.cfg["long_press_ms"]:
                            self._long_fired = True
                            app.handle_long_press()
                    time.sleep(0.01)
            except Exception:
                pass
            finally:
                try: dev.close()
                except Exception: pass
                self._device     = None
                self._press_time = None
                app.device_connected = False
                app.update_tray()
                while not self._queue.empty():
                    try: self._queue.get_nowait()
                    except: break
                time.sleep(1)

# ── Main App ───────────────────────────────────────────────────────────────────
class PowerMateApp:
    def __init__(self):
        self.cfg              = load_config()
        self.volume           = VolumeController()
        self.running          = True
        self.device_connected = False
        self.tray             = None
        self._settings_proc   = None

    def handle_rotation(self, device, delta):
        if self.cfg["invert_rotation"]:
            delta = -delta
        self.volume.adjust(delta * self.cfg["volume_step"] * self.cfg["sensitivity"])
        self.update_tray()
        if self.cfg["led_pulse_on_volume"] and device:
            threading.Thread(target=self._pulse_led, args=(device,), daemon=True).start()

    def _pulse_led(self, device):
        b0 = self.cfg["led_brightness"]
        for b in [255, 80, b0]:
            set_led(device, b)
            time.sleep(0.06)

    def handle_click(self):
        action = self.cfg["click_action"]
        if   action == "mute":       self.volume.mute_toggle(); self.update_tray()
        elif action == "play_pause": media_key(VK_MEDIA_PLAY_PAUSE)
        elif action == "next_track": media_key(VK_MEDIA_NEXT_TRACK)
        elif action == "prev_track": media_key(VK_MEDIA_PREV_TRACK)

    def handle_long_press(self):
        action = self.cfg["long_press_action"]
        if   action == "mute":       self.volume.mute_toggle(); self.update_tray()
        elif action == "media_stop": media_key(VK_MEDIA_STOP)
        elif action == "play_pause": media_key(VK_MEDIA_PLAY_PAUSE)

    def update_tray(self):
        vol   = self.volume.get()
        muted = self.volume.is_muted()
        write_status(vol, muted)
        # Reload config in case MAUI app saved changes
        self.cfg = load_config()
        if self.tray:
            self.tray.icon  = make_tray_icon(muted, vol)
            status = "Muted" if muted else f"Volume: {vol}%"
            conn   = "Connected" if self.device_connected else "Disconnected"
            self.tray.title = f"PowerMate — {status} ({conn})"
            self.tray.update_menu()

    def open_settings(self, icon=None, it=None):
        # If already open, bring to front by doing nothing (MAUI handles single instance)
        if self._settings_proc and self._settings_proc.poll() is None:
            return
        vol   = self.volume.get()
        muted = self.volume.is_muted()
        conn  = self.device_connected
        startup = get_startup()
        args = [
            SETTINGS_EXE,
            "--config",    CONFIG_FILE,
            "--volume",    str(vol),
            "--muted",     str(muted).lower(),
            "--connected", str(conn).lower(),
            "--startup",   str(startup).lower(),
        ]
        try:
            self._settings_proc = subprocess.Popen(
                args,
                creationflags=subprocess.CREATE_NO_WINDOW
            )
        except FileNotFoundError:
            # EXE not built yet — show a helpful message via tray notification
            if self.tray:
                self.tray.notify(
                    "Settings app not found",
                    "Run: dotnet publish PowerMateSettings\\PowerMateSettings.csproj"
                )

    def quit_app(self, icon=None, it=None):
        self.running = False
        if self._settings_proc:
            try: self._settings_proc.terminate()
            except Exception: pass
        if self.tray:
            self.tray.stop()

    def build_menu(self):
        vol   = self.volume.get()
        muted = self.volume.is_muted()
        conn  = self.device_connected
        return pystray.Menu(
            item("● Device connected" if conn else "○ Device not found",
                 None, enabled=False),
            item("Muted" if muted else f"{vol}% volume",
                 None, enabled=False),
            pystray.Menu.SEPARATOR,
            item("Settings…",          self.open_settings, default=True),
            item("Start with Windows", self._toggle_startup,
                 checked=lambda _: get_startup()),
            pystray.Menu.SEPARATOR,
            item("Quit", self.quit_app),
        )

    def _toggle_startup(self, icon, it):
        set_startup(not get_startup())

    def _status_writer(self):
        """Write volume status every second for MAUI app to poll."""
        while self.running:
            write_status(self.volume.get(), self.volume.is_muted())
            time.sleep(1)

    def run(self):
        threading.Thread(target=PowerMateDriver(self).run, daemon=True).start()
        threading.Thread(target=self._status_writer, daemon=True).start()
        self.tray = pystray.Icon(
            "powermate",
            make_tray_icon(self.volume.is_muted(), self.volume.get()),
            "PowerMate Driver",
            menu=pystray.Menu(lambda: self.build_menu())
        )
        self.tray.run()


if __name__ == "__main__":
    PowerMateApp().run()
