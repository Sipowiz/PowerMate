# Griffin PowerMate Driver

A clean, modern Windows driver for the Griffin PowerMate USB knob.
No .NET 2.0, no ancient software — just Python.

---

## Quick Start

1. Make sure **Python 3.7+** is installed
2. Run the installer:
   ```
   python installer.py
   ```
3. The installer handles all pip packages and offers to launch at startup

That's it. The driver lives in your system tray.

---

## Files

| File | Purpose |
|---|---|
| `installer.py` | GUI installer — run this first |
| `powermate_tray.py` | The actual driver (tray app) |
| `powermate_config.json` | Settings (auto-created / edited via Settings UI) |

---

## System Tray

Right-click the tray icon for:
- Current volume / device status
- Open Settings
- Toggle "Start with Windows"
- Quit

Double-click to open Settings.

---

## Configuration

All settings are in the Settings window (double-click tray icon).

| Setting | Options | Default |
|---|---|---|
| Volume step | 1–10% | 2% |
| Sensitivity | 1–5x | 1x |
| Invert rotation | on/off | off |
| Single click | mute / play_pause / next / prev / none | mute |
| Long press | none / media_stop / play_pause / mute | none |
| Long press threshold | 300–2000ms | 800ms |
| LED brightness | 0–255 | 255 |
| LED pulse on change | on/off | off |
| Start with Windows | on/off | — |
| Volume notifications | on/off | on |

---

## Manual Startup (without installer)

```
pythonw powermate_tray.py
```

`pythonw` = no console window. Regular `python` works too but shows a terminal.

---

## Troubleshooting

**"Device not found"** — Plug in the PowerMate. If already plugged in, try running as Administrator (right-click → Run as administrator).

**No pip / Python** — Download Python from python.org. Make sure to check "Add Python to PATH" during install.
