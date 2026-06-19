# Mutation Hook Spike

## Goal

Make the real Explorer-owned Windows 11 tray icon stack use two rows, without overlaying, masking, or cloning icons.

## Route

This spike uses the XAML Diagnostics TAP route instead of blind remote-thread injection.

`TrayHookController` calls `InitializeXamlDiagnosticsEx` from `Windows.UI.Xaml.dll`. Microsoft documents this API as the entry point for XAML Diagnostics; it loads a TAP DLL into the target process and creates a COM object implementing `IObjectWithSite`.

`TrayHook.Native` is that TAP DLL. Its COM class receives the XAML Diagnostics site, subscribes to visual-tree changes, converts visual-tree handles back to XAML `FrameworkElement` objects, finds `NotificationAreaIcons`, then mutates its `ItemsPresenter -> StackPanel` children:

- rows: `2`
- slot width: `24`
- arrangement: column-first, top-to-bottom
- mutation: content presenter width/height plus `TranslateTransform`

This is aimed at the real XAML elements, not a copied visual overlay.

Double mode also promotes tray entries in:

```text
HKCU\Control Panel\NotifyIconSettings
```

For each entry, it sets `IsPromoted=1` when missing or disabled and touches a temporary value so Explorer's existing registry watchers notice the change. A lightweight worker continues checking for newly created entries every two seconds while double mode is active.

Explorer splits tray visuals across sibling XAML stacks. `NotificationAreaIcons` contains promoted app icons, but `NotifyIconStack`, `MainStack`, `NonActivatableStack`, and `ControlCenterButton` can own neighboring tray/system icons such as IME/input, emoji/keyboard, focus/display/audio, and chevron-like tray surfaces. The TAP now applies a `SystemTrayFrameGrid` pass that sends those sibling stacks through the same compact two-row grid path instead of only wrapping `NotificationAreaIcons`.

The sibling-stack pass also reserves the full two-row taskbar height on the stack/container path and clears `UIElement.Clip`. This prevents system icons from becoming hoverable but partially hidden because their original parent still measured as a one-row tray control.

## Current Build State

Verified on this PC:

```cmd
dotnet build src\TrayHookController\TrayHookController.csproj
dotnet run --project src\TrayHookController\TrayHookController.csproj
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe" src\TrayHook.Native\TrayHook.Native.vcxproj /p:Configuration=Release /p:Platform=x64 /m
```

The native TAP DLL now builds locally. It exports the COM entrypoints required by XAML Diagnostics:

```cmd
dumpbin /exports src\TrayHook.Native\x64\Release\TrayHook.Native.dll
```

## Native Build Command

```cmd
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe" src\TrayHook.Native\TrayHook.Native.vcxproj /p:Configuration=Release /p:Platform=x64 /m
```

Expected TAP DLL path:

```text
src\TrayHook.Native\x64\Release\TrayHook.Native.dll
```

## Apply Command

Only run this after the native DLL builds:

```cmd
dotnet run --project src\TrayHookController\TrayHookController.csproj -- --apply --dll src\TrayHook.Native\x64\Release\TrayHook.Native.dll
```

## Reset Command

Use this to clear the tray layout mutation without restarting Explorer:

```cmd
dotnet run --project src\TrayHookController\TrayHookController.csproj -- --apply --reset --dll src\TrayHook.Native\x64\Release\TrayHook.Native.dll
```

The native TAP logs to:

```text
%TEMP%\SystrayWrapDoubler.Native.log
```

Current verified log shape for apply:

```text
DllGetClassObject called
TAP factory CreateInstance called
TAP SetSite called
Promoted notify icon settings: changed=16 checked=72
Promotion worker started
Applied measured grid: children=11, presenters=11, rows=2, width=24, itemHeight=24.0, cols=6
TAP attached: mode=double rows=2 width=24
```

Current verified log shape for reset:

```text
DllGetClassObject called
TAP factory CreateInstance called
TAP SetSite called
Reset grid mutation: children=6, reset=6
TAP attached: mode=reset rows=1 width=32
```

Reset disables the promotion worker and clears live layout transforms. It intentionally does not delete `IsPromoted` values because this project does not keep registry backups and those values can overlap with settings the user intentionally chose.

## References

- Microsoft `InitializeXamlDiagnosticsEx`: https://learn.microsoft.com/windows/win32/api/xamlom/nf-xamlom-initializexamldiagnosticsex
- Windhawk tray spacing/grid reference implementation: https://mods.windhawk.net/mods/taskbar-notification-icon-spacing.wh.cpp
- XAML Diagnostics TAP demo: https://gist.github.com/m417z/8741e52d8eaad67b47ee365a20070bf8
