# Griffin PowerMate Driver — MAUI Edition

## Requirements

- Python 3.7+ with packages: `pywinusb pycaw comtypes Pillow pystray`
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

---

## Build the Settings UI (once)

Open a terminal in this folder and run:

```powershell
dotnet publish PowerMateSettings\PowerMateSettings.csproj `
  -c Release `
  -f net10.0-windows10.0.19041.0 `
  --self-contained true `
  -o PowerMateSettings\bin\publish
```

This produces a self-contained `PowerMateSettings.exe` — no .NET runtime needed on the target machine.

Update the `SETTINGS_EXE` path in `powermate_tray.py` if needed:
```python
SETTINGS_EXE = os.path.join(BASE_DIR, "PowerMateSettings", "bin", "publish", "PowerMateSettings.exe")
```

---

## Run

```powershell
py powermate_tray.py
```

The tray icon appears in the system tray. Double-click or right-click → Settings to open the native MAUI settings window.

---

## How it works

```
powermate_tray.py  (Python, always running)
  │
  ├── pywinusb     → reads HID events from the knob
  ├── pycaw        → controls Windows master volume
  ├── pystray      → system tray icon + menu
  │
  ├── writes powermate_status.json every second
  │     {"volume": 47, "muted": false}
  │
  └── launches PowerMateSettings.exe on demand
        │
        ├── reads  powermate_config.json  (settings)
        ├── reads  powermate_status.json  (live volume display)
        └── writes powermate_config.json  on Save
```

Python handles everything time-sensitive (HID polling, volume).
MAUI handles the UI — launched as a separate process, no threading conflicts.
