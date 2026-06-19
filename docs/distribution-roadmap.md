# Systray Wrap Doubler Distribution Roadmap

Goal: make Systray Wrap Doubler discoverable where people already search when Windows 11 system-tray icons will not stack into two rows.

## Roadmap

🟥 Build a working installer EXE
🟥 Include source code and documentation inside the installer payload
⬛ Publish one canonical project home
⬛ Add a release download with screenshots
⬛ Post transparent answers in existing threads where people already asked for this
⬛ Submit to Windows utility/software directories
⬛ Add package-manager entries after the public URL is stable
⬛ Create a short demo video/GIF after the public page exists

## Canonical Home First

Create one trusted place that every post links back to.

Recommended first home:

- GitHub repository
- GitHub release with `SystrayDoubler Installer.exe`
- README, screenshots, source, docs, license, release notes

Optional mirrors after GitHub:

- SourceForge
- Internet Archive software item
- personal site or simple landing page

Do this before forum posting so every answer points to the same stable URL.

## Search Terms To Target

Use these naturally in README, release title, forum titles, and descriptions:

- Windows 11 two row system tray icons
- Windows 11 system tray icons two rows
- Windows 11 notification area icons two rows
- Windows 11 tray icons grid
- Windows 11 taskbar tray icons wrap
- Windows 11 show all tray icons
- Windows 11 system tray overflow arrow
- Windhawk tray icon grid alternative
- standalone Windows tray icon grid utility
- Windows 11 restore stacked notification icons

## Existing Threads Worth Answering

These are not fake posts. They are existing places where people asked the same kind of question.

- ElevenForum: "Stack Notification Icons on Taskbar System Tray in Windows 11 Broken by Update"
- ElevenForum: "is there any way to force the system tray icons to a 2nd row..."
- Microsoft Q&A: "how do i have two rows in my taskbar"
- Microsoft Q&A: "Can Windows 11 display multiple rows in the taskbar? How?"
- Reddit r/Windows11: "System Tray --- 2 or more Rows? HOW???"
- Reddit r/Windows11: "Still no way to increase Windows11 taskbar height"
- Super User: "How can I make Windows' system tray two rows instead of one?"
- Windhawk GitHub issues/discussions only if the post is framed as source/code for people who want to adapt it, not as spam.

## Windhawk Positioning

Be respectful and clear:

- Windhawk already has related mods such as tray icon spacing/grid and always-show-tray-icons.
- Systray Wrap Doubler is a standalone installer for people who want this specific behavior without setting up a broader mod framework.
- The installer includes source code, so people can inspect it, fork it, or adapt the approach into a Windhawk mod.
- Do not post as "better than Windhawk." Post as "another option, source included."

## Posting Order

1. GitHub repo and release.
2. SourceForge mirror.
3. ElevenForum existing threads.
4. Reddit r/Windows11/r/WindowsHelp/r/Windhawk.
5. Microsoft Q&A answers where allowed.
6. Super User only if the answer is technically detailed and disclosure is clear.
7. MajorGeeks/Softpedia/SnapFiles submissions.
8. WinGet/Chocolatey/Scoop after the GitHub release URL is stable.
9. Product Hunt/Show HN only after the page looks polished.

## Rules For Posting

- Be transparent: "I made this."
- Do not pretend to be a random user asking and answering yourself.
- Do not spam the same text everywhere.
- On forums, answer the exact question first, then link.
- Mention that it changes Explorer's live tray layout and includes a revert/uninstall path.
- Mention Windows version support honestly after testing.
- Invite people to inspect the source included in the install folder.

## Short Public Description

Systray Wrap Doubler is a small Windows 11 utility that makes system-tray icons use a compact two-row layout. It is for people who keep many tray icons visible and do not want Windows to hide them behind the overflow arrow or let the tray consume the taskbar. The installer includes the app, native hook, documentation, and full source code.
