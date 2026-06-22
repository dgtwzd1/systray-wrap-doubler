# Systray Wrap Doubler

Systray Wrap Doubler gives Windows 11 notification-area/system-tray icons a compact two-row layout.

It exists for people who keep many tray icons visible and do not want Windows to shove them into the overflow arrow or let the tray eat half the taskbar. The goal is simple: use the vertical space already taken by the clock/date area and let tray icons wrap into two usable rows.

## Download

Latest release:

```text
https://github.com/dgtwzd1/systray-wrap-doubler/releases/latest
```

## Download Trust Notice

Systray Wrap Doubler is a new, independently published open-source Windows utility. This release may not be digitally signed with an established publisher certificate, so Microsoft Defender SmartScreen or a browser may identify the installer as an unrecognized app.

That warning is expected for many new or unsigned Windows downloads, but it should still be taken seriously. Only download Systray Wrap Doubler from the official GitHub repository, review the source code if you want to verify what it does, and run it only if you personally trust the project and understand that it modifies live Windows Explorer taskbar UI state.

The full source code is available in this repository and is also included in the installed `source` folder for review, auditing, forking, or rebuilding. If you are not comfortable with an unsigned Windows shell customization utility, do not install it.

## Where To Review The Source Code

The source is not hidden behind the installer. It is available in two places:

- In this GitHub repository under `src`: [review the source code here](https://github.com/dgtwzd1/systray-wrap-doubler/tree/main/src)
- After install, under `C:\Program Files\Systray Wrap Doubler\source` unless you chose a different install folder

Useful starting points:

- `src\SystrayWrapDoubler.App` contains the tray app, menu commands, apply/revert logic, and recovery behavior.
- `src\TrayHook.Native\TrayHook.Native.cpp` contains the native Explorer/XAML layout hook that changes the live tray layout.
- `src\SystrayWrapDoubler.Installer` contains the installer and payload extraction code.
- `src\SystrayWrapDoubler.Uninstaller` contains the uninstall and cleanup path.
- `scripts\package.ps1` contains the release packaging script and shows exactly what is bundled.

## What It Does

- Applies a two-row Windows 11 system-tray icon layout.
- Promotes tray icons so hidden/new app icons can join the visible tray.
- Keeps its own control icon visible in the tray.
- Provides Apply 2 Rows, Revert, Restart Shell, and Exit commands from the tray menu and main window.
- Reverts before exiting or uninstalling.
- Includes the source code and documentation in the install folder.

## Install

Run the installer:

```text
SystrayDoubler Installer.exe
```

The installer defaults to:

```text
C:\Program Files\Systray Wrap Doubler
```

You can choose another install location. The installed folder contains:

- `SystrayWrapDoubler.exe`
- `TrayHook.Native.dll`
- `Uninstall.exe`
- `README.md`
- `docs`
- `source`

## Use

After launching Systray Wrap Doubler, right-click its tray icon.

- `Apply 2 Rows` applies the compact two-row tray layout.
- `Revert` resets Explorer's live tray layout back to normal and restores recorded tray visibility choices when available.
- `Restart Shell` restarts Explorer.
- `Exit` attempts to revert before closing so the tray is not abandoned in a modified state.

## Uninstall

Use either:

- Windows Settings > Apps > Installed apps
- Start Menu > Systray Wrap Doubler > Uninstall Systray Wrap Doubler
- `Uninstall.exe` in the install folder

Uninstall runs the revert path first, removes shortcuts and registry uninstall entries, cleans Systray Wrap Doubler temp/state files, removes its Windows notification-history entry, and deletes the install folder.

## Recovery

If Explorer looks wrong after a crash or forced close, run:

```text
SystrayWrapDoubler.exe --reset --restore-promotion-state --restart-shell
```

You can also reinstall and choose `Revert` from the app.

Reset is deterministic even when there is no saved previous-state file: it attaches a fresh reset hook to Explorer, disables the promotion worker, clears the live layout transform, and only restores recorded icon visibility when a state file exists.

## How It Works

Windows 11's taskbar system tray is implemented with XAML inside `explorer.exe`. Systray Wrap Doubler uses Microsoft's XAML Diagnostics attachment path to load a small native TAP DLL into the current Explorer taskbar process. That DLL locates the live system-tray XAML containers and changes their layout at runtime.

The app also updates per-user notification icon settings under:

```text
HKCU\Control Panel\NotifyIconSettings
```

That step makes visible-tray placement possible for icons Windows would otherwise keep in the overflow menu.

## Safety Notes

This is a Windows customization utility. It touches live Explorer UI state and per-user notification icon settings. Use it only if you are comfortable with Windows shell customization tools.

The app is not affiliated with Microsoft. It was built because Windows 11 does not currently provide a normal built-in setting for two-row system-tray icons.

## Open Source

This project was made by Al with help from ChatGPT and Codex. The installed folder includes the full source code so others can learn from it, improve it, fork it, or take it further.

If you want to audit the program before running it, start with the `src` folder in this repository or the installed `source` folder. The important runtime pieces are listed in "Where To Review The Source Code" above.

If you share improvements and want to give a shout-out, that would be appreciated, but the important part is that people who need this can find it.

## Project Ethos

This project follows a simple builder rule: useful tools should be clear, honest, recoverable, and easy to leave.

Systray Wrap Doubler is free in this release, source-included, and built without dark patterns. If someone wants to improve it, fork it, or adapt the approach into another open-source tool such as a Windhawk mod, take the torch.

See [docs/project-ethos.md](docs/project-ethos.md).

For transparent posting/discovery language, see [docs/discovery-kit.md](docs/discovery-kit.md).
