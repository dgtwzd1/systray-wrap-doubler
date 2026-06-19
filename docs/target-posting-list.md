# Target Posting List

Use this after the GitHub repository/release exists. Replace `<GitHub release URL here>` in templates before posting.

## Priority 1: Canonical Source

1. GitHub repository
   - Purpose: source of truth, issue tracker, release download.
   - Post: README, screenshots, release notes, installer.

2. GitHub release
   - Asset: `SystrayDoubler Installer.exe`
   - Title: `Systray Wrap Doubler v0.1.0`
   - Body: use `docs/posting-templates.md` > GitHub Release Body.

## Priority 2: Existing Help Threads

These are the highest-value posts because they answer actual existing questions.

1. ElevenForum: Stack Notification Icons on Taskbar System Tray in Windows 11 Broken by Update
   - https://www.elevenforum.com/t/stack-notification-icons-on-taskbar-system-tray-in-windows-11-broken-by-update.13064/
   - Angle: "Registry keys stopped working; I made a standalone tool."

2. ElevenForum: force system tray icons to a 2nd row
   - https://www.elevenforum.com/t/is-their-anyway-to-force-the-system-tray-icons-to-a-2nd-row-instead-of-the-steam-icon-hanging-partly-off-the-screen.26846/
   - Angle: "This is exactly the problem: tray icons need a second row instead of clipping/hanging."

3. Microsoft Q&A: how do I have two rows in my taskbar
   - https://learn.microsoft.com/en-us/answers/questions/5755199/how-do-i-have-two-rows-in-my-taskbar
   - Angle: "Windows does not support it natively; here is a third-party/open-source option if allowed."

4. Microsoft Q&A: Can Windows 11 display multiple rows in the taskbar?
   - https://learn.microsoft.com/en-us/answers/questions/4376505/can-windows-11-display-multiple-rows-in-the-taskba
   - Angle: "For system tray specifically, this utility may help."

5. Super User: How can I make Windows' system tray two rows instead of one?
   - https://superuser.com/questions/1050587/how-can-i-make-windows-system-tray-two-rows-instead-of-one
   - Angle: "Old answer applies to Windows 7/10. For Windows 11, here is a different tool-based approach."

## Priority 3: Windhawk / Modding Spaces

Post only with contributor framing, not as an ad.

1. Windhawk subreddit / related Reddit threads
   - Mention Windhawk has related mods.
   - Say source is included and the approach can be adapted.

2. Windhawk GitHub issues/discussions only where relevant
   - Example issue: system icons in grid request
   - https://github.com/ramensoftware/windhawk-mods/issues/3214
   - Angle: "I built a standalone source-included proof around this behavior; sharing for anyone interested in adapting the approach."

3. Windhawk related mod pages to reference, not replace
   - Tray icon spacing/grid: https://windhawk.net/mods/taskbar-notification-icon-spacing
   - Always show all tray icons: https://windhawk.net/mods/taskbar-notification-icons-show-all
   - Multirow taskbar note: https://windhawk.net/mods/taskbar-multirow

## Priority 4: Reddit Discovery

Use short, honest posts.

- r/Windows11
- r/WindowsHelp
- r/Windows
- r/Windhawk
- r/software
- r/opensource

Potential title:

```text
I made a free/source-included utility for two-row Windows 11 system tray icons
```

## Priority 5: Software Directories

Do after GitHub has screenshots and release notes.

- MajorGeeks
- Softpedia
- SnapFiles
- SourceForge
- AlternativeTo

## Priority 6: Package Managers

Do after the download URL is stable and at least one public release is tested.

- WinGet Community Repository
- Chocolatey
- Scoop

## Posting Rule

Every post should include:

- "I made this" disclosure.
- "Free/source-included" statement.
- Compatibility/recovery warning.
- Direct relevance to the thread.
- One canonical download/source link.
- "If this is not appropriate here, feel free to remove it" when posting in communities sensitive to promotion.
