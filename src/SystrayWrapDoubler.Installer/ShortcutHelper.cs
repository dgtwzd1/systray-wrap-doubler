namespace SystrayWrapDoubler.Installer;

internal static class ShortcutHelper
{
    public static void CreateDesktopShortcut(string appPath)
    {
        var shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Systray Wrap Doubler.lnk");
        CreateShortcut(shortcutPath, appPath, Path.GetDirectoryName(appPath)!);
    }

    public static void CreateStartMenuShortcuts(string appPath, string uninstallerPath)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            "Systray Wrap Doubler");
        Directory.CreateDirectory(folder);

        CreateShortcut(
            Path.Combine(folder, "Systray Wrap Doubler.lnk"),
            appPath,
            Path.GetDirectoryName(appPath)!);

        CreateShortcut(
            Path.Combine(folder, "Uninstall Systray Wrap Doubler.lnk"),
            uninstallerPath,
            Path.GetDirectoryName(uninstallerPath)!);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)
            ?? throw new InvalidOperationException("Could not create WScript.Shell.");
        dynamic shortcut = shell.GetType().InvokeMember(
            "CreateShortcut",
            System.Reflection.BindingFlags.InvokeMethod,
            null,
            shell,
            [shortcutPath])!;

        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.IconLocation = $"{targetPath},0";
        shortcut.Description = "Systray Wrap Doubler";
        shortcut.Save();
    }
}
