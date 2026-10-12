<p align="center">
  <img src="assets/virtual-desktop.svg" width="96" alt="Virtual Display Tray icon">
</p>

<h1 align="center">Virtual Display Tray</h1>

<p align="center">
  One click in the system tray gives your headless GPU a virtual display.<br>
  No HDMI dummy plug, no dashboard, no reboot.
</p>

<p align="center">
  <a href="https://virtualdisplaytray.znidaric.si/"><b>Website</b></a> ·
  <a href="../../releases/latest"><b>Download</b></a> ·
  <a href="#building-from-source"><b>Build</b></a>
</p>

---

Virtual Display Tray is a small Windows tray app that turns a virtual monitor on and off. Windows, games and remote-desktop tools (Parsec, Sunshine/Moonlight, RDP, VNC and similar) see it as a real display, so a GPU with no physical monitor attached still renders at full resolution.

It bundles an [Indirect Display (IddCx)](https://learn.microsoft.com/windows-hardware/drivers/display/indirect-display-driver-model-overview) driver and installs or removes the device with the same SetupAPI calls Microsoft's `devcon` uses. Nothing else to download.

## Features

- **One-click toggle.** Left-click the tray icon to switch the virtual display on or off. Red dot = off, green = on, yellow = working.
- **Single self-contained `.exe`.** The driver files are embedded and unpacked on first run.
- **Starts with Windows.** A scheduled task runs it elevated at login, with no UAC prompt each time.
- **Many resolutions.** 640×480 up to 8K, configurable in [`driver/option.txt`](driver/option.txt).
- **Clean uninstall.** One menu item removes the device, driver, scheduled task and all files.
- **Logging.** Every action is timestamped in a log you can open from the tray menu.
- **Tiny.** About 185 KB. Built with the C# compiler already included in Windows (.NET Framework 4.x).

## Requirements

- Windows 10 or 11, 64-bit
- .NET Framework 4.x (preinstalled on Windows 10/11)
- Administrator rights (needed to install a driver)

## Install

1. Download `VirtualDisplayTray.exe` from the [latest release](../../releases/latest) or the [website](https://virtualdisplaytray.znidaric.si/).
2. Run it and approve the UAC prompt.
3. On first run it:
   - copies itself and the driver to `C:\ProgramData\VirtualDisplayTray`
   - trusts the driver's certificate
   - registers the `VirtualDisplayTray` scheduled task so it starts at login
4. A dot appears in the system tray. If you don't see it, click the `^` arrow next to the clock.

## Usage

| Action | Result |
|---|---|
| Left-click tray icon | Turn the virtual display on/off |
| Right-click → **Open log file** | Show the timestamped log |
| Right-click → **Uninstall completely** | Remove the device, driver, scheduled task and `C:\ProgramData\VirtualDisplayTray` (asks first) |
| Right-click → **Exit (keep installed)** | Close the tray icon for this session only. It comes back at the next login |

Once the display is on, set its resolution and refresh rate in **Settings → System → Display** like any other monitor.

### Changing available resolutions

[`driver/option.txt`](driver/option.txt) controls the modes the driver offers. The first line is the number of virtual monitors. Each line after that is `width, height, refresh`. After editing, rebuild the exe. On an installed copy, you can edit `C:\ProgramData\VirtualDisplayTray\option.txt` directly and toggle the display off and on.

## Driver signing and Secure Boot

The bundled driver uses a **self-signed certificate**, not a Microsoft WHQL signature. The app trusts that certificate on first run (the same as [`driver/installCert.bat`](driver/installCert.bat)). If Windows still won't load the driver, typically because of Secure Boot or a driver-signing policy:

- disable Secure Boot in your UEFI/BIOS, **or**
- enable test signing from an elevated prompt and reboot:

  ```bash
  bcdedit /set testsigning on
  ```

## Building from source

You need nothing beyond Windows itself. `Build.cmd` finds `csc.exe` from .NET Framework 4.x and compiles everything, including the embedded driver files, into one exe.

```bash
Build.cmd
```

The output goes to `bin\VirtualDisplayTray.exe`.

GitHub Actions builds every push. Pushing a `v*` tag (for example `v1.0.0`) creates a GitHub Release with the exe attached.

## Repository layout

```
├── Build.cmd                 # one-step build, no SDK required
├── src/
│   ├── Program.cs            # tray app + SetupAPI device installer
│   └── app.manifest          # requests admin (UAC shield)
├── driver/                   # IddCx virtual display driver (embedded into the exe)
│   ├── IddSampleDriver.inf / .dll / .cat
│   ├── Virtual_Display_Driver.cer
│   ├── option.txt            # monitor count + resolution list
│   └── installCert.bat       # manual certificate trust helper
├── assets/                   # app and tray icons
├── scripts/
│   ├── VirtualDisplayTray.cmd       # legacy single-file Batch/PowerShell version
│   └── RemoveOldVirtualDisplay.cmd  # standalone cleanup for leftover devices
└── docs/                     # project website (GitHub Pages ready)
```

### Script-only version

[`scripts/VirtualDisplayTray.cmd`](scripts/VirtualDisplayTray.cmd) is the earlier version of the app as a single Batch/PowerShell file. The driver is embedded as Base64, and the behavior and tray menu are the same. It's useful if you prefer readable script over a compiled exe.

[`scripts/RemoveOldVirtualDisplay.cmd`](scripts/RemoveOldVirtualDisplay.cmd) removes any leftover `Root\IddSampleDriver` devices, for example ones left behind by other virtual display tools.

## Troubleshooting

Right-click the tray icon → **Open log file**. Every install and remove attempt is logged with the exact Windows error. The log lives at `C:\ProgramData\VirtualDisplayTray\VirtualDisplayTray.log`. Please attach it when you [open an issue](../../issues/new/choose).

## Credits and license

The tray app, build scripts and website are released under the [MIT License](LICENSE).

The bundled driver is a build of Microsoft's IddCx *IddSampleDriver* sample, as extended by the open-source community. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
