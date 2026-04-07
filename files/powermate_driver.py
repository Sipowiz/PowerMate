"""
Griffin PowerMate Windows Driver
Reads HID events and controls master volume / other actions.
"""

import hid
import json
import os
import sys
import time
import threading
import ctypes
from ctypes import cast, POINTER
from comtypes import CLSCTX_ALL
from pycaw.pycaw import AudioUtilities, IAudioEndpointVolume

CONFIG_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "powermate_config.json")

DEFAULT_CONFIG = {
    "volume_step": 2,           # % per tick
    "sensitivity": 1,           # multiplier (1 = normal, 2 = faster)
    "invert_rotation": False,   # flip clockwise/counterclockwise
    "click_action": "mute",     # "mute" | "play_pause" | "none"
    "long_press_action": "none",# "none" | "media_stop"
    "long_press_ms": 800,       # ms to trigger long press
    "led_brightness": 255,      # 0-255 (PowerMate LED)
    "led_pulse_on_volume": False # pulse LED when volume changes
}

POWERMATE_VENDOR_ID  = 0x077D
POWERMATE_PRODUCT_ID = 0x0410

def load_config():
    if os.path.exists(CONFIG_FILE):
        try:
            with open(CONFIG_FILE) as f:
                cfg = json.load(f)
            # Fill in any missing keys from defaults
            for k, v in DEFAULT_CONFIG.items():
                cfg.setdefault(k, v)
            return cfg
        except Exception:
            pass
    return DEFAULT_CONFIG.copy()

def save_config(cfg):
    with open(CONFIG_FILE, "w") as f:
        json.dump(cfg, f, indent=2)

def find_powermate():
    for device in hid.enumerate():
        if device["vendor_id"] == POWERMATE_VENDOR_ID and device["product_id"] == POWERMATE_PRODUCT_ID:
            return device["path"]
    return None

class VolumeController:
    def __init__(self):
        devices = AudioUtilities.GetSpeakers()
        interface = devices.Activate(IAudioEndpointVolume._iid_, CLSCTX_ALL, None)
        self.volume = cast(interface, POINTER(IAudioEndpointVolume))

    def get_volume(self):
        return round(self.volume.GetMasterVolumeLevelScalar() * 100)

    def set_volume(self, pct):
        pct = max(0, min(100, pct))
        self.volume.SetMasterVolumeLevelScalar(pct / 100.0, None)

    def adjust_volume(self, delta_pct):
        current = self.get_volume()
        self.set_volume(current + delta_pct)

    def toggle_mute(self):
        muted = self.volume.GetMute()
        self.volume.SetMute(not muted, None)

    def is_muted(self):
        return bool(self.volume.GetMute())

def send_media_key(vk_code):
    """Send a media key via keybd_event."""
    KEYEVENTF_KEYUP = 0x0002
    ctypes.windll.user32.keybd_event(vk_code, 0, 0, 0)
    ctypes.windll.user32.keybd_event(vk_code, 0, KEYEVENTF_KEYUP, 0)

VK_MEDIA_PLAY_PAUSE = 0xB3
VK_MEDIA_STOP       = 0xB2

def set_led(device, brightness):
    """Set PowerMate LED brightness (0-255)."""
    try:
        # PowerMate LED control: report ID 0, 9 bytes
        # Byte 0: report id, Byte 4: brightness
        report = [0x00] * 9
        report[0] = 0x00  # report id
        report[4] = int(brightness) & 0xFF
        device.write(report)
    except Exception:
        pass

class PowerMateDriver:
    def __init__(self):
        self.cfg = load_config()
        self.vol = VolumeController()
        self.running = False
        self._press_time = None
        self._long_press_fired = False

    def reload_config(self):
        self.cfg = load_config()
        print(f"[PowerMate] Config reloaded.")

    def handle_rotation(self, device, delta):
        """delta is +1 or -1 per tick from the device."""
        if self.cfg["invert_rotation"]:
            delta = -delta
        step = self.cfg["volume_step"] * self.cfg["sensitivity"]
        change = delta * step
        self.vol.adjust_volume(change)
        vol = self.vol.get_volume()
        muted = self.vol.is_muted()
        status = "MUTED" if muted else f"{vol}%"
        print(f"[PowerMate] Volume: {status}  (delta {change:+})")

        if self.cfg["led_pulse_on_volume"]:
            threading.Thread(target=self._pulse_led, args=(device,), daemon=True).start()

    def _pulse_led(self, device):
        for b in [255, 100, 200, self.cfg["led_brightness"]]:
            set_led(device, b)
            time.sleep(0.05)

    def handle_button_down(self):
        self._press_time = time.time()
        self._long_press_fired = False

    def handle_button_up(self, device):
        if self._press_time is None:
            return
        held_ms = (time.time() - self._press_time) * 1000
        self._press_time = None

        if self._long_press_fired:
            return

        action = self.cfg["click_action"]
        print(f"[PowerMate] Click ({held_ms:.0f}ms) → {action}")
        if action == "mute":
            self.vol.toggle_mute()
            muted = self.vol.is_muted()
            print(f"[PowerMate] {'Muted' if muted else 'Unmuted'}")
        elif action == "play_pause":
            send_media_key(VK_MEDIA_PLAY_PAUSE)
        # "none" → do nothing

    def check_long_press(self, device):
        """Called in a loop to detect long press."""
        if self._press_time and not self._long_press_fired:
            held_ms = (time.time() - self._press_time) * 1000
            if held_ms >= self.cfg["long_press_ms"]:
                self._long_press_fired = True
                action = self.cfg["long_press_action"]
                print(f"[PowerMate] Long press → {action}")
                if action == "media_stop":
                    send_media_key(VK_MEDIA_STOP)

    def run(self):
        path = find_powermate()
        if not path:
            print("[PowerMate] Device not found. Is it plugged in?")
            sys.exit(1)

        print(f"[PowerMate] Device found. Starting driver...")
        print(f"[PowerMate] Volume step: {self.cfg['volume_step']}%  |  Click: {self.cfg['click_action']}  |  Long press: {self.cfg['long_press_action']}")
        print(f"[PowerMate] Press Ctrl+C to stop.\n")

        self.running = True
        try:
            with hid.Device(path=path) as device:
                device.nonblocking = True
                set_led(device, self.cfg["led_brightness"])

                while self.running:
                    data = device.read(6)
                    if data:
                        # Byte 0: button state (1=pressed, 0=released)
                        # Byte 1: rotation (signed, +1 CW, -1 CCW as int8)
                        button = data[0] & 0x01
                        raw_rot = data[1]
                        # Convert to signed int8
                        rotation = raw_rot if raw_rot < 128 else raw_rot - 256

                        if button == 1 and self._press_time is None:
                            self.handle_button_down()
                        elif button == 0 and self._press_time is not None:
                            self.handle_button_up(device)

                        if rotation != 0:
                            ticks = rotation  # can be ±1, ±2 etc
                            for _ in range(abs(ticks)):
                                self.handle_rotation(device, 1 if ticks > 0 else -1)

                    self.check_long_press(device)
                    time.sleep(0.005)  # 5ms polling

        except KeyboardInterrupt:
            print("\n[PowerMate] Stopped.")
        except Exception as e:
            print(f"[PowerMate] Error: {e}")

if __name__ == "__main__":
    driver = PowerMateDriver()
    driver.run()
