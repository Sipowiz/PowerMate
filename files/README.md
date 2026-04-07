# Griffin PowerMate Windows Driver

A lightweight Python driver for the Griffin PowerMate USB knob on Windows.
Controls master volume by default, with configurable click/long-press actions.

## Requirements

- Python 3.7+
- Two pip packages:

```
pip install hid pycaw comtypes
```

> On some systems you may also need: `pip install hidapi`

## Usage

```
python powermate_driver.py
```

Press **Ctrl+C** to stop.

To run silently in the background (no console window):
```
pythonw powermate_driver.py
```

To auto-start on Windows login, add a shortcut to `powermate_driver.py`
in your Startup folder: press `Win+R` → `shell:startup`.

---

## Configuration (`powermate_config.json`)

Edit `powermate_config.json` in the same folder as the script.
Changes take effect on next launch.

| Key | Default | Description |
|---|---|---|
| `volume_step` | `2` | Volume % change per tick of the knob |
| `sensitivity` | `1` | Multiplier on volume_step (e.g. 2 = double speed) |
| `invert_rotation` | `false` | Swap CW/CCW direction |
| `click_action` | `"mute"` | Single click: `"mute"`, `"play_pause"`, `"none"` |
| `long_press_action` | `"none"` | Long press: `"none"`, `"media_stop"` |
| `long_press_ms` | `800` | How long to hold for a long press (milliseconds) |
| `led_brightness` | `255` | PowerMate LED brightness (0 = off, 255 = full) |
| `led_pulse_on_volume` | `false` | Flash LED briefly when volume changes |

### Example: faster knob, play/pause on click

```json
{
  "volume_step": 3,
  "sensitivity": 1,
  "invert_rotation": false,
  "click_action": "play_pause",
  "long_press_action": "media_stop",
  "long_press_ms": 700,
  "led_brightness": 180,
  "led_pulse_on_volume": true
}
```

---

## Troubleshooting

**"Device not found"** — Make sure the PowerMate is plugged in.
On some systems you need to run as Administrator for HID access.

**No volume change** — Try running as Administrator once to rule out permissions.

**LED not working** — Some PowerMate firmware versions ignore LED commands. This is harmless.
