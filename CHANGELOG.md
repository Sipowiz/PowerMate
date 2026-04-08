# Changelog

All notable changes to this project will be documented in this file.

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
