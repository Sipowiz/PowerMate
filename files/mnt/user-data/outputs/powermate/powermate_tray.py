"""
Griffin PowerMate - System Tray Application
Settings UI uses customtkinter for native Win11 look.
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
from PIL import Image, ImageDraw
import pystray
from pystray import MenuItem as item
from comtypes import CLSCTX_ALL
from ctypes import cast, POINTER
from pycaw.pycaw import AudioUtilities, IAudioEndpointVolume
import customtkinter as ctk

# ── Appearance ─────────────────────────────────────────────────────────────────
ctk.set_appearance_mode("dark")
ctk.set_default_color_theme("blue")

# ── Paths ──────────────────────────────────────────────────────────────────────
BASE_DIR    = os.path.dirname(os.path.abspath(__file__))
CONFIG_FILE = os.path.join(BASE_DIR, "powermate_config.json")
REG_KEY     = r"Software\Microsoft\Windows\CurrentVersion\Run"
REG_VALUE   = "GriffinPowerMate"

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

def save_config(cfg):
    with open(CONFIG_FILE, "w") as f:
        json.dump(cfg, f, indent=2)

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

# ── Settings Window ────────────────────────────────────────────────────────────
class SettingsWindow:
    def __init__(self, app):
        self.app  = app
        self._open = False

    def open(self):
        if self._open:
            return
        self._open = True
        try:
            self._build()
        finally:
            self._open = False

    def _build(self):
        cfg = load_config()

        win = ctk.CTk()
        win.title("PowerMate Settings")
        win.geometry("460x640")
        win.resizable(False, False)

        # ── Header ─────────────────────────────────────────────────────────────
        header = ctk.CTkFrame(win, fg_color="#0d0d0d", corner_radius=0, height=64)
        header.pack(fill="x")
        header.pack_propagate(False)

        # Knob canvas
        import tkinter as tk
        knob = tk.Canvas(header, width=38, height=38, bg="#0d0d0d",
                          highlightthickness=0)
        knob.place(x=20, y=13)
        knob.create_oval(2, 2, 36, 36, outline="#3b8ed0", width=3)
        knob.create_oval(13, 13, 25, 25, fill="#3b8ed0", outline="")

        ctk.CTkLabel(header, text="Griffin PowerMate",
                     font=ctk.CTkFont(size=16, weight="bold"),
                     text_color="#ffffff").place(x=70, y=12)
        ctk.CTkLabel(header, text="Driver Settings",
                     font=ctk.CTkFont(size=12),
                     text_color="#555555").place(x=70, y=36)

        # Connection pill
        conn     = self.app.device_connected
        pill_col = "#1a4a1a" if conn else "#4a1a1a"
        pill_txt = "● Connected" if conn else "○ Disconnected"
        pill_ftxt= "#44cc66" if conn else "#cc4444"
        ctk.CTkLabel(header, text=pill_txt,
                     font=ctk.CTkFont(size=11),
                     fg_color=pill_col, text_color=pill_ftxt,
                     corner_radius=10).place(relx=1.0, x=-16, y=20, anchor="ne")

        # ── Scrollable body ─────────────────────────────────────────────────────
        scroll = ctk.CTkScrollableFrame(win, fg_color="transparent",
                                         label_text="")
        scroll.pack(fill="both", expand=True, padx=16, pady=(12, 0))

        # ── Section helper ──────────────────────────────────────────────────────
        def section(parent, title):
            ctk.CTkLabel(parent, text=title,
                         font=ctk.CTkFont(size=10),
                         text_color="#666666").pack(anchor="w", pady=(12, 4))
            frame = ctk.CTkFrame(parent, corner_radius=10)
            frame.pack(fill="x", pady=(0, 4))
            return frame

        def divider(parent):
            ctk.CTkFrame(parent, height=1, fg_color="#2a2a2a",
                          corner_radius=0).pack(fill="x", padx=16)

        # ── Slider row ──────────────────────────────────────────────────────────
        def slider_row(parent, label, value, from_, to, unit, first=False):
            if not first:
                divider(parent)
            row = ctk.CTkFrame(parent, fg_color="transparent")
            row.pack(fill="x", padx=16, pady=10)
            ctk.CTkLabel(row, text=label,
                         font=ctk.CTkFont(size=13),
                         text_color="#cccccc").pack(side="left")
            val_lbl = ctk.CTkLabel(row, text=f"{value}{unit}",
                                    font=ctk.CTkFont(size=13, weight="bold"),
                                    text_color="#3b8ed0", width=48)
            val_lbl.pack(side="right")
            var = ctk.DoubleVar(value=value)

            def on_slide(v):
                iv = int(float(v))
                val_lbl.configure(text=f"{iv}{unit}")
                var.set(iv)

            inner = ctk.CTkFrame(parent, fg_color="transparent")
            inner.pack(fill="x", padx=16, pady=(0, 10))
            ctk.CTkSlider(inner, from_=from_, to=to, variable=var,
                           command=on_slide).pack(fill="x")
            return var

        # ── Combo row ───────────────────────────────────────────────────────────
        def combo_row(parent, label, value, values, display_map, first=False):
            if not first:
                divider(parent)
            row = ctk.CTkFrame(parent, fg_color="transparent")
            row.pack(fill="x", padx=16, pady=10)
            ctk.CTkLabel(row, text=label,
                         font=ctk.CTkFont(size=13),
                         text_color="#cccccc").pack(side="left")
            display_vals = [display_map[v] for v in values]
            current_disp = display_map.get(value, display_vals[0])
            var = ctk.StringVar(value=current_disp)
            ctk.CTkComboBox(row, values=display_vals, variable=var,
                             state="readonly", width=180).pack(side="right")
            # Store reverse map on var for saving
            var._raw_values   = values
            var._display_vals = display_vals
            return var

        def get_raw(var):
            disp = var.get()
            if disp in var._display_vals:
                return var._raw_values[var._display_vals.index(disp)]
            return var._raw_values[0]

        # ── Toggle row ──────────────────────────────────────────────────────────
        def toggle_row(parent, label, value, first=False):
            if not first:
                divider(parent)
            row = ctk.CTkFrame(parent, fg_color="transparent")
            row.pack(fill="x", padx=16, pady=10)
            ctk.CTkLabel(row, text=label,
                         font=ctk.CTkFont(size=13),
                         text_color="#cccccc").pack(side="left")
            var = ctk.BooleanVar(value=value)
            ctk.CTkSwitch(row, text="", variable=var, width=44).pack(side="right")
            return var

        CLICK_MAP = {
            "mute":        "Mute / Unmute",
            "play_pause":  "Play / Pause",
            "next_track":  "Next Track",
            "prev_track":  "Previous Track",
            "none":        "None",
        }
        LONG_MAP = {
            "none":        "None",
            "mute":        "Mute / Unmute",
            "media_stop":  "Stop Media",
            "play_pause":  "Play / Pause",
        }

        # ── ROTATION ────────────────────────────────────────────────────────────
        s = section(scroll, "ROTATION")
        v_step   = slider_row(s, "Volume step",  cfg["volume_step"],  1, 10, "%", first=True)
        v_sens   = slider_row(s, "Sensitivity",  cfg["sensitivity"],  1,  5, "x")
        v_invert = toggle_row(s, "Invert direction", cfg["invert_rotation"])

        # ── BUTTON ACTIONS ───────────────────────────────────────────────────────
        s = section(scroll, "BUTTON ACTIONS")
        v_click = combo_row(s, "Single click", cfg["click_action"],
                             list(CLICK_MAP.keys()), CLICK_MAP, first=True)
        v_long  = combo_row(s, "Long press",   cfg["long_press_action"],
                             list(LONG_MAP.keys()), LONG_MAP)
        v_lms   = slider_row(s, "Long press threshold", cfg["long_press_ms"],
                              300, 2000, "ms")

        # ── LED ─────────────────────────────────────────────────────────────────
        s = section(scroll, "LED")
        v_led   = slider_row(s, "Brightness", cfg["led_brightness"], 0, 255, "", first=True)
        v_pulse = toggle_row(s, "Pulse on volume change", cfg["led_pulse_on_volume"])

        # ── SYSTEM ──────────────────────────────────────────────────────────────
        s = section(scroll, "SYSTEM")
        v_startup = toggle_row(s, "Start with Windows", get_startup(),  first=True)
        v_notifs  = toggle_row(s, "Volume notifications", cfg["notifications"])

        # ── Footer ──────────────────────────────────────────────────────────────
        footer = ctk.CTkFrame(win, fg_color="#0d0d0d", corner_radius=0, height=64)
        footer.pack(fill="x", side="bottom")
        footer.pack_propagate(False)

        vol_lbl = ctk.CTkLabel(footer, text="",
                     font=ctk.CTkFont(size=11),
                     text_color="#555555")
        vol_lbl.place(x=16, rely=0.5, anchor="w")

        def refresh_vol():
            if not win.winfo_exists():
                return
            v  = self.app.volume.get()
            m  = self.app.volume.is_muted()
            vol_lbl.configure(text="Muted" if m else f"Volume: {v}%")
            win.after(500, refresh_vol)

        win.after(100, refresh_vol)

        saved_lbl = ctk.CTkLabel(footer, text="✓ Saved",
                                  font=ctk.CTkFont(size=11),
                                  text_color="#44cc66")

        def do_save():
            new_cfg = load_config()
            new_cfg["volume_step"]         = int(v_step.get())
            new_cfg["sensitivity"]         = int(v_sens.get())
            new_cfg["invert_rotation"]     = v_invert.get()
            new_cfg["click_action"]        = get_raw(v_click)
            new_cfg["long_press_action"]   = get_raw(v_long)
            new_cfg["long_press_ms"]       = int(v_lms.get())
            new_cfg["led_brightness"]      = int(v_led.get())
            new_cfg["led_pulse_on_volume"] = v_pulse.get()
            new_cfg["notifications"]       = v_notifs.get()
            save_config(new_cfg)
            set_startup(v_startup.get())
            self.app.cfg = new_cfg
            self.app.update_tray()
            saved_lbl.place(relx=0.5, rely=0.5, anchor="center")
            win.after(2000, saved_lbl.place_forget)

        ctk.CTkButton(footer, text="Cancel", width=90, height=34,
                       fg_color="#2a2a2a", hover_color="#3a3a3a",
                       text_color="#aaaaaa",
                       command=win.destroy).place(relx=1.0, x=-112, rely=0.5, anchor="e")

        ctk.CTkButton(footer, text="Save", width=90, height=34,
                       command=do_save).place(relx=1.0, x=-16, rely=0.5, anchor="e")

        # Center on screen
        win.update_idletasks()
        w  = win.winfo_width()
        h  = win.winfo_height()
        sw = win.winfo_screenwidth()
        sh = win.winfo_screenheight()
        win.geometry(f"+{(sw-w)//2}+{(sh-h)//2}")

        win.mainloop()


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
        self._settings_win    = SettingsWindow(self)

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
        if self.tray:
            vol   = self.volume.get()
            muted = self.volume.is_muted()
            self.tray.icon  = make_tray_icon(muted, vol)
            status = "Muted" if muted else f"Volume: {vol}%"
            conn   = "Connected" if self.device_connected else "Disconnected"
            self.tray.title = f"PowerMate — {status} ({conn})"
            self.tray.update_menu()

    def open_settings(self, icon=None, item=None):
        threading.Thread(target=self._settings_win.open, daemon=False).start()

    def quit_app(self, icon=None, item=None):
        self.running = False
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

    def run(self):
        threading.Thread(target=PowerMateDriver(self).run, daemon=True).start()
        self.tray = pystray.Icon(
            "powermate",
            make_tray_icon(self.volume.is_muted(), self.volume.get()),
            "PowerMate Driver",
            menu=pystray.Menu(lambda: self.build_menu())
        )
        self.tray.run()


if __name__ == "__main__":
    PowerMateApp().run()
