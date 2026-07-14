# Changelog

All notable changes to this project will be documented in this file.

## [1.4.9] - 2026-07-14

### Fixed
- LED audio-pulse stayed dark after the render stream stopped — when playback ended, the output device switched, or a monitor slept, the WASAPI loopback capture stopped on its own and nothing restarted it, so bass pulsing froze until a settings change re-armed it. `OnCaptureStopped` also raced the default-device-change restart, which only restarted if it still saw a live capture at that instant. Capture is now self-healing: the pulse tick re-acquires it (at most once per second) whenever a pulse is configured but capture is down, and start/stop are serialized so a restart can never leave two live captures

### Changed
- Fast-forward/rewind is much gentler — the knob emits many detents per turn, so even the old 1 s/detent minimum scrubbed too fast. Seek is now sub-second per detent, chosen from a **Slow / Medium / Fast** preset (0.25 / 0.5 / 1.0 s) instead of a 1–30 s slider
- Settings page trimmed from ~13 controls to the essentials: the "Step per tick" and "Sensitivity" sliders merged into one Sensitivity control; the Multi-click window and Long-press duration sliders were removed (their proven defaults stay in code); the Bass frequency-cutoff and gain sliders were removed (Bass-only is now a single toggle)

## [1.4.8] - 2026-07-09

### Fixed
- Fast-forward/rewind jumped around and looped instead of seeking smoothly ([#4](https://github.com/Sipowiz/PowerMate/issues/4)) — each detent fired a fire-and-forget `SeekRelativeAsync` that re-read the timeline from SMTC, which keeps reporting the pre-seek position for tens of milliseconds afterwards, so overlapping seeks all computed from the same stale base. FF/RW now captures the position **once** when the gesture starts, accumulates detents into a signed offset, and a single pump applies the latest absolute target with at most one seek in flight
- The 1.4.7 rotation fix made this worse before it made it better: decoding every detent (rather than dropping the batched ones) meant a fast spin issued 2–4× more overlapping seeks
- Track position on the LED and tray icon stuttered during FF/RW, because both read the lagging SMTC position rather than where the user was seeking to
- Seeking continued after the knob was unplugged mid-gesture, and a track auto-advancing mid-gesture would scrub the new track

### Changed
- `IMediaSessionService.SeekRelativeAsync` replaced by the stateless, absolute `SeekToAsync`, plus `GetPosition()`, `GetDuration()` and `GetSessionGeneration()`. The caller owns the anchor, so the media layer no longer writes any position back
- Sessions that refuse a seek are detected once and left alone, instead of being hammered on every detent

## [1.4.7] - 2026-07-09

### Fixed
- Installer's "Start with Windows" option was not respected — the installer registered autostart with a Startup-folder shortcut (`{userstartup}\PowerMate Driver.lnk`) while the app read and wrote the `HKCU\...\CurrentVersion\Run` value, so the in-app toggle could never disable what the installer enabled, and enabling both launched two instances. Both now drive the same Run value, and the settings toggle reads live registry state instead of a cached copy in `config.json`
- Fast rotation lost most of the knob's movement — the device reports a *signed* delta in byte 2 that reaches ±4 when detents are batched between polls, but only ±1 was decoded. Measured against real hardware, 36% of rotation reports (58% of actual detents) were being silently discarded
- Single click sometimes triggered Next Track — a disconnect while the button was held left stale button state in both `HidService` and `PowerMateController`, so the first report after reconnecting was counted as an extra tap ([#2](https://github.com/Sipowiz/PowerMate/issues/2))
- Bass "Live level" meter never filled past ~20% — the bar bound a 0–100 percentage directly to `WidthRequest`, which is measured in device-independent pixels, not percent ([#5](https://github.com/Sipowiz/PowerMate/issues/5))
- LED brightness slider did nothing — the setting was saved, clamped and unit-tested, but no code ever read it. It now caps the LED range for every indicator (volume, audio pulse, track position)
- Uninstalling left an orphaned `Run` registry value pointing at the deleted executable
- Uninstall entry showed a generic icon — `UninstallDisplayIcon` pointed at a path that does not exist after install
- Changing any unrelated setting rewrote or deleted the autostart registry value as a side effect of `AutoSave`

### Added
- Single-instance guard — a second launch now exits immediately instead of opening the HID device alongside the first, which made every knob event register twice
- Autostart path is written quoted, so the default install location under Program Files cannot be misparsed

### Changed
- The installer's "Start with Windows" checkbox is now **opt-in** (previously pre-ticked). Existing choices are preserved on upgrade
- Default `LedBrightness` is now 255 (full range), matching how the LED behaved before the slider was wired up. A previously saved value will now take effect
- `StartWithWindows` was removed from `config.json`; the Windows registry is the single source of truth. Old config files load unchanged

## [1.4.6] - 2026-05-26

### Fixed
- Device disconnects when setting LED brightness — replaced Win32 P/Invoke calls (`CreateFile`, `HidD_SetFeature`, `HidD_SetOutputReport`, `WriteFile`) with HidSharp async stream write, eliminating handle conflicts that caused the HID device to disconnect; volume knob is now instantly responsive after brightness changes

## [1.4.5] - 2026-05-21

### Fixed
- App force-killed during hibernate with no crash log — `WasapiLoopbackCapture.StopRecording()` and `Dispose()` were called synchronously on the WndProc thread; when the audio driver shuts down concurrently during hibernate the COM call blocks, Windows' power-suspend timeout expires, and the process is killed before any managed handler runs; teardown is now fire-and-forget on a thread pool thread so the WndProc returns immediately

### Added
- Startup log entry (`PowerMate x.y.z starting`) written on every launch, creating the log file immediately so future hibernation post-mortems can confirm which version was running and that logging is functional

## [1.4.4] - 2026-05-20

### Fixed
- App killed during sleep/hibernate with no crash log — managed exception escaping the native WndProc callback (`OnWindowMessage`) caused CLR FailFast (`STATUS_FATAL_APP_EXIT`, 0xC000027B), bypassing all managed exception handlers; added try/catch inside the callback so power-broadcast exceptions are caught and logged instead

### Added
- Serilog rolling daily log files at `%AppData%\PowerMate\crash-YYYYMMDD.log` (30-day retention) replacing the custom crash log writer
- Custom `Program.cs` entry point wraps `Application.Start()` in try/catch/finally with `Log.CloseAndFlush()` in the finally block — guarantees the log is flushed however the app exits
- WinUI `UnhandledException` handler logs and suppresses XAML-layer exceptions before they can terminate the process

## [1.4.3] - 2026-05-20

### Fixed
- App killed by Windows during hibernate — HID stream now closed before suspend (`HidService.Suspend()`) so Windows can safely suspend the USB bus; stream reconnects automatically on resume

## [1.4.2] - 2026-05-20

### Fixed
- App killed by Windows during hibernate when default audio output is a monitor — device removal now triggers immediate capture teardown via `DefaultAudioRenderDeviceChanged`, preventing the app from holding a stale WASAPI reference through suspend
- `RecordingStopped` now handled so NAudio device-gone exceptions are caught instead of propagating on the capture thread
- Capture restarts automatically on the new default device after a device-change event (e.g. monitor reconnect on resume)

## [1.4.1] - 2026-05-20

### Fixed
- Accidental volume change immediately after releasing from FF/RW — 500 ms input guard blocks rotation on button release

## [1.4.0] - 2026-05-20

### Added
- Survive sleep/hibernate: audio capture stops before suspend and restarts (with 2 s delay for WASAPI reinit) on resume
- Crash logging to `%AppData%\PowerMate\crash.log` for unhandled exceptions and unobserved task exceptions
- Tests for crash logging and power management suspend/resume

### Changed
- Audio-pulse LED falls back to static volume indicator when playback is paused or stopped; resumes pulsing when playback starts again
- App icon and shortcuts now use a transparent-background knob design (no dark rounded rectangle)
- Installer shortcuts point to `powermate.ico` explicitly; shell icon cache flushed post-install

## [1.3.0] - 2026-05-19

### Added
- Fast-forward / rewind by holding button and rotating, with configurable seek step (1–30s)
- Interaction mode system (Idle, Volume, Button, FF/RW) with tray icon state reflection
- SMTC media session integration — playback state tracking and symbol flash on skip
- FF/RW seek step slider in settings UI

### Changed
- Button actions simplified to fixed mapping (click=play/pause, double=next, triple=previous, long=mute)
- Audio-reactive LED now uses WASAPI loopback capture with RMS energy — volume-independent and near-zero latency
- LED pulse timer reduced from 80ms to 20ms for snappier response
- Bass mode decay tuned for sustained low-frequency glow while keeping transient punch

### Fixed
- Crash when toggling pulse LED switch (COM apartment mismatch accessing AudioMeterInformation from thread pool)
- Config save now uses atomic write (tmp + move) to prevent corruption

## [1.2.1] - 2026-04-08

### Fixed
- Taskbar icon still showing .NET logo in Release builds — deferred WM_SETICON to after WinUI default icon setup

## [1.2.0] - 2026-04-08

### Added
- Configurable multi-click window (tap sensitivity) slider (150–800ms)
- System volume change monitoring — LED and tray icon update when volume is changed from other apps
- Rotation suppression during multi-tap sequences to prevent dropped clicks

### Changed
- "Mute" renamed to "Mute / Unmute" in all action pickers for clarity

### Fixed
- Volume control stopping after extended use (COM reference invalidation due to GC collecting MMDevice)
- Multi-tap race condition causing inconsistent double/triple click detection
- Taskbar icon not displaying in Release builds (switched from file-based SetIcon to WM_SETICON with HICON handle)
- Volume feedback loop when system notifications re-triggered our own changes

## [1.1.0] - 2026-04-08

### Added
- Credits / About page with GitHub and LinkedIn links, accessible from the system tray
- Update checker — polls GitHub releases and shows a download button when a new version is available
- Window position memory — settings window reopens where you last placed it
- Custom `powermate.ico` for the installer and window taskbar
- 64-bit architecture check in the installer (clear error message on 32-bit Windows)

### Fixed
- Taskbar icon showing the default .NET logo in Release builds — now sets `powermate.ico` immediately on window creation

## [1.0.0] - 2026-04-07

### Added
- Volume control via knob rotation with configurable step size, sensitivity, and invert option
- Single, double, and triple click actions (play/pause, next track, previous track) — all configurable
- Long press action (mute, play/pause, or none) with configurable threshold
- LED brightness reflects current volume level in real time
- Audio-reactive LED mode — LED pulses to line-out audio peaks
- Bass-only LED mode with FFT analysis, configurable frequency cutoff and gain
- Live bass level meter in settings UI
- System tray icon with blue volume arc and mute indicator
- Dark-themed MAUI/WinUI settings window with debounced auto-save
- Start with Windows option
- Inno Setup installer with desktop shortcut and uninstall support
- 24 unit tests (xUnit + NSubstitute)
- GitHub Actions CI/CD pipeline with automated installer releases on version tags
