using Microsoft.Win32;

namespace SystrayWrapDoubler.Installer;

internal static class InstallRegistry
{
    private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SystrayWrapDoubler";

    public static void Write(string installPath, string appPath, string uninstallerPath)
    {
        using var key = Registry.LocalMachine.CreateSubKey(UninstallPath, writable: true);
        if (key is null)
        {
            throw new InvalidOperationException("Could not create uninstall registry entry.");
        }

        key.SetValue("DisplayName", "Systray Wrap Doubler", RegistryValueKind.String);
        key.SetValue("DisplayVersion", "0.1.0", RegistryValueKind.String);
        key.SetValue("Publisher", "Al with ChatGPT and Codex", RegistryValueKind.String);
        key.SetValue("InstallLocation", installPath, RegistryValueKind.String);
        key.SetValue("DisplayIcon", appPath, RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{uninstallerPath}\" --install-dir \"{installPath}\"", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --silent --install-dir \"{installPath}\"", RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }
}
