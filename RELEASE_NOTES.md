# Systray Wrap Doubler v0.1.0

First public MVP release.

## What It Does

Systray Wrap Doubler gives Windows 11 system-tray/notification-area icons a compact two-row layout.

It is meant for users who keep many tray icons visible and want the tray to use the same vertical space already occupied by the clock/date area instead of hiding icons in the overflow arrow or consuming too much horizontal taskbar space.

## Included

- `SystrayWrapDoubler.exe` Windows tray app
- `TrayHook.Native.dll` native XAML Diagnostics hook
- `Uninstall.exe`
- README and documentation
- full source code in the install folder

## Source Code Location

The source code is included for review. In the GitHub repository, start with `src`. After installation, the same source bundle is copied to the installed `source` folder, usually:

```text
https://github.com/dgtwzd1/systray-wrap-doubler/tree/main/src
```

```text
C:\Program Files\Systray Wrap Doubler\source
```

The main runtime code is in `src\SystrayWrapDoubler.App`, the native Explorer/XAML hook is in `src\TrayHook.Native\TrayHook.Native.cpp`, and the packaging script is `scripts\package.ps1`.

## Main Features

- Apply two-row tray layout.
- Revert live Explorer tray layout.
- Restart Explorer shell.
- Keep Systray Wrap Doubler's own tray icon visible.
- Recover tray icon after Explorer restarts.
- Revert before app exit or uninstall.
- Clean uninstall path for install folder, shortcuts, state files, temp files, notification-history entry, and installer extraction cache.

## Important Notes

This is a Windows shell customization tool. It changes live Explorer XAML layout state and per-user notification icon visibility settings. Use it only if you are comfortable with Windows customization utilities.

This is a new, independently published open-source release and may not be digitally signed with an established publisher certificate. Microsoft Defender SmartScreen or a browser may identify the installer as unrecognized. Only download it from the official GitHub repository, review the included source code if desired, and run it only if you personally trust the project.

The app is not affiliated with Microsoft.

## Recovery

If needed, run:

```text
SystrayWrapDoubler.exe --reset --restore-promotion-state --restart-shell
```

## Windhawk / Modding Note

Windhawk has related taskbar and tray mods. Systray Wrap Doubler is a standalone installer and source bundle for this specific two-row tray behavior. The source is included so anyone can inspect it, fork it, or adapt the approach into a Windhawk mod.
