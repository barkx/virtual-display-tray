# Changelog

## [1.0.0] - 2026-10-12

First public release.

- Tray app with a one-click toggle for the virtual display (red/green/yellow status dot)
- Self-contained exe with the IddCx driver embedded
- First run installs to `C:\ProgramData\VirtualDisplayTray`, trusts the driver certificate, and registers an elevated auto-start scheduled task
- Tray menu: Open log file, Uninstall completely, Exit (keep installed)
- `Build.cmd` builds with the .NET Framework `csc.exe` included in Windows
- Legacy single-file `VirtualDisplayTray.cmd` and the `RemoveOldVirtualDisplay.cmd` cleanup tool
