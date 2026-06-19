# Posting Templates

Use these as starting points. Rewrite a little for each site so it answers the thread directly instead of looking pasted everywhere.

## GitHub Repository Description

Windows 11 utility that gives system-tray/notification-area icons a compact two-row layout. Includes installer, source, docs, revert, and clean uninstall path.

## GitHub Release Body

Systray Wrap Doubler v0.1.0 is the first public MVP.

It gives Windows 11 system-tray icons a compact two-row layout, meant for people who keep many tray icons visible and do not want Windows to hide them behind the overflow arrow or let the tray consume a huge stretch of taskbar.

The installer includes:

- tray app
- native Explorer/XAML hook DLL
- uninstaller
- documentation
- full source code

Use the tray icon menu:

- Apply 2 Rows
- Revert
- Restart Shell
- Exit

Important: this is a Windows shell customization utility. It changes live Explorer XAML layout state and per-user notification icon visibility settings. Use it only if you are comfortable with Windows customization tools.

Windhawk note: Windhawk has related taskbar/tray mods. This project is a standalone installer/source bundle for this specific behavior. The source is included so anyone can inspect it, fork it, or adapt the approach into a Windhawk mod.

## General Forum Answer

I ran into this same Windows 11 system-tray problem and ended up making a small utility for it.

It is called Systray Wrap Doubler. It makes the Windows 11 notification-area/system-tray icons use a compact two-row layout instead of staying in one long row or being pushed into the overflow arrow.

It has a tray menu with:

- Apply 2 Rows
- Revert
- Restart Shell
- Exit

The installer includes the source code and docs in the install folder, so people can inspect it or improve it. It also has an uninstall/revert path so it does not intentionally leave the tray stuck modified.

This is a Windows shell customization tool, so only use it if you are comfortable with Explorer/taskbar customization utilities.

Project/download:

`<GitHub release URL here>`

## Windhawk-Oriented Post

I made a standalone proof/app for the Windows 11 two-row system-tray icon problem and wanted to share it here because it may be useful to Windhawk users or mod authors.

It is called Systray Wrap Doubler. It uses a native Explorer/XAML hook to make the real Windows 11 tray icons use a compact two-row layout. It also promotes visible tray icons so new/custom app icons can join the visible tray area.

This is not meant as a "Windhawk is bad" post. Windhawk has related mods and is probably the right home for many people. I am sharing this because the installer includes the full source code, so anyone can inspect the approach, fork it, or adapt the logic into a Windhawk mod if they want.

The app includes:

- Apply 2 Rows
- Revert
- Restart Shell
- Exit with revert guard
- clean uninstall path
- source/docs included in the install folder

Download/source:

`<GitHub release URL here>`

If this is not appropriate for this space, feel free to remove it. I am posting because it is free, source-included, and directly related to the tray/grid behavior people have been discussing.

## Reddit Short Post

I made a small Windows 11 utility for people who want system tray icons in two rows.

It is called Systray Wrap Doubler. It gives notification-area/tray icons a compact two-row layout, with a tray menu for Apply, Revert, Restart Shell, and Exit. The installer includes the source code and docs, so it can be inspected or adapted.

This is a shell customization tool, so it is for people who are comfortable with Explorer/taskbar tweaks.

It is free and source-included. If this kind of post is not allowed here, feel free to remove it.

`<GitHub release URL here>`

## Microsoft Q&A Style Answer

Windows 11 does not currently offer a normal built-in setting for this, but I made a standalone utility that may help if third-party shell customization is acceptable for your machine.

Systray Wrap Doubler makes Windows 11 system-tray/notification-area icons use a compact two-row layout. It includes a Revert command and uninstaller, and the installer includes the full source code and documentation.

Because this changes live Explorer/taskbar layout behavior, do not use it on managed/work machines unless your organization allows this kind of utility.

`<GitHub release URL here>`

## Super User Style Disclosure

Disclosure: I made this utility.

For Windows 11, the old drag-to-resize/taskbar-row behavior is not available in the same way as Windows 7/10. I wrote a small utility called Systray Wrap Doubler that changes the live Windows 11 tray layout so notification-area icons can use two rows.

It is not a registry-only tweak. It uses an Explorer/XAML hook and includes Revert/Uninstall paths. Source code is included.

`<GitHub release URL here>`
