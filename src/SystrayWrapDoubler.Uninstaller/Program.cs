using System.Diagnostics;
using Microsoft.Win32;

namespace SystrayWrapDoubler.Uninstaller;

internal static class Program
{
    private const string AppProcessName = "SystrayWrapDoubler";
    private const string DisplayName = "Systray Wrap Doubler";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try
        {
            var options = UninstallOptions.Parse(args);
            if (options.FinalDeletePath is not null)
            {
                FinalDelete(options.FinalDeletePath, options.CleanupSelf);
                return 0;
            }

            var installDir = Path.GetFullPath(options.InstallDirectory ?? AppContext.BaseDirectory);
            if (!PathGuard.IsSafeInstallDirectory(installDir))
            {
                MessageBox.Show(
                    $"Refusing to uninstall from an unsafe folder:\n{installDir}",
                    DisplayName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 2;
            }

            if (!options.Silent && MessageBox.Show(
                    "Uninstall Systray Wrap Doubler?\n\nThe app will revert the live tray layout, restore recorded tray icon visibility choices, remove shortcuts, and delete the install folder.",
                    DisplayName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return 0;
            }

            RevertInstalledApp(installDir);
            StopRunningApp(installDir);
            RemoveShortcuts();
            RemoveUninstallRegistry();
            RemoveNotificationHistory(installDir);
            CleanupUserState();
            StartFinalDelete(installDir);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void RevertInstalledApp(string installDir)
    {
        var appPath = Path.Combine(installDir, "SystrayWrapDoubler.exe");
        if (!File.Exists(appPath))
        {
            return;
        }

        RunAndWait(appPath, "--reset --restore-promotion-state --cleanup-temp --restart-shell", installDir, 30000);
    }

    private static void StopRunningApp(string installDir)
    {
        var currentProcessId = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName(AppProcessName))
        {
            using (process)
            {
                if (process.Id == currentProcessId || !ProcessBelongsToInstall(process, installDir))
                {
                    continue;
                }

                try
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(3000))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                }
                catch
                {
                    // Continue uninstall cleanup.
                }
            }
        }
    }

    private static bool ProcessBelongsToInstall(Process process, string installDir)
    {
        try
        {
            var processPath = process.MainModule?.FileName;
            return processPath is not null &&
                   processPath.StartsWith(installDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void RemoveShortcuts()
    {
        var shortcutNames = new[]
        {
            "Systray Wrap Doubler.lnk",
            "Uninstall Systray Wrap Doubler.lnk"
        };

        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                "Systray Wrap Doubler")
        };

        foreach (var folder in folders)
        {
            foreach (var shortcutName in shortcutNames)
            {
                TryDeleteFile(Path.Combine(folder, shortcutName));
            }

            TryDeleteDirectoryIfEmpty(folder);
        }
    }

    private static void RemoveUninstallRegistry()
    {
        TryDeleteRegistrySubKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SystrayWrapDoubler");
        TryDeleteRegistrySubKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SystrayWrapDoubler");
    }

    private static void CleanupUserState()
    {
        TryDeleteFile(Path.Combine(Path.GetTempPath(), "SystrayWrapDoubler.Native.log"));
        TryDeleteDirectory(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SystrayWrapDoubler"));
        RemoveDotNetExtractionCaches();
    }

    private static void RemoveNotificationHistory(string installDir)
    {
        using var baseKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", writable: true);
        if (baseKey is null)
        {
            return;
        }

        var entriesToDelete = new List<string>();
        foreach (var subKeyName in baseKey.GetSubKeyNames())
        {
            using var subKey = baseKey.OpenSubKey(subKeyName);
            var executablePath = subKey?.GetValue("ExecutablePath") as string;
            var initialTooltip = subKey?.GetValue("InitialTooltip") as string;

            if (MatchesInstalledAppNotification(executablePath, initialTooltip, installDir))
            {
                entriesToDelete.Add(subKeyName);
            }
        }

        foreach (var entry in entriesToDelete)
        {
            try
            {
                baseKey.DeleteSubKeyTree(entry, throwOnMissingSubKey: false);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    private static bool MatchesInstalledAppNotification(string? executablePath, string? initialTooltip, string installDir)
    {
        if (!string.Equals(initialTooltip, DisplayName, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var expectedExe = Path.Combine(installDir, "SystrayWrapDoubler.exe");
        if (string.Equals(executablePath, expectedExe, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return executablePath.EndsWith(
            @"\Systray Wrap Doubler\SystrayWrapDoubler.exe",
            StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveDotNetExtractionCaches()
    {
        var dotNetTemp = Path.Combine(Path.GetTempPath(), ".net");
        if (!Directory.Exists(dotNetTemp))
        {
            return;
        }

        foreach (var path in Directory.EnumerateDirectories(dotNetTemp))
        {
            var name = Path.GetFileName(path);
            if (string.Equals(name, "SystrayDoubler Installer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "SystrayWrapDoubler", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Uninstall", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("SystrayWrapDoubler-Uninstall-", StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteDirectory(path);
            }
        }
    }

    private static void StartFinalDelete(string installDir)
    {
        var finalizerPath = Path.Combine(
            Path.GetTempPath(),
            $"SystrayWrapDoubler-Uninstall-{Guid.NewGuid():N}.exe");

        File.Copy(Application.ExecutablePath, finalizerPath, overwrite: true);
        var args = $"--final-delete {Quote(installDir)} --cleanup-self";
        Process.Start(new ProcessStartInfo(finalizerPath, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static void FinalDelete(string installDir, bool cleanupSelf)
    {
        installDir = Path.GetFullPath(installDir);
        if (!PathGuard.IsSafeInstallDirectory(installDir))
        {
            return;
        }

        for (var attempt = 0; attempt < 40; attempt++)
        {
            StopRunningApp(installDir);
            try
            {
                if (Directory.Exists(installDir))
                {
                    Directory.Delete(installDir, recursive: true);
                }

                break;
            }
            catch
            {
                Thread.Sleep(500);
            }
        }

        if (cleanupSelf)
        {
            QueueSelfDeletion();
        }
    }

    private static void QueueSelfDeletion()
    {
        var batchPath = Path.Combine(Path.GetTempPath(), $"SystrayWrapDoubler-Cleanup-{Guid.NewGuid():N}.cmd");
        var exePath = Application.ExecutablePath;
        File.WriteAllText(
            batchPath,
            "@echo off\r\n" +
            "timeout /t 2 /nobreak > nul\r\n" +
            $"del /f /q {Quote(exePath)} > nul 2> nul\r\n" +
            "rmdir /s /q \"%TEMP%\\.net\\SystrayDoubler Installer\" > nul 2> nul\r\n" +
            "rmdir /s /q \"%TEMP%\\.net\\SystrayWrapDoubler\" > nul 2> nul\r\n" +
            "rmdir /s /q \"%TEMP%\\.net\\Uninstall\" > nul 2> nul\r\n" +
            "for /d %%D in (\"%TEMP%\\.net\\SystrayWrapDoubler-Uninstall-*\") do rmdir /s /q \"%%D\" > nul 2> nul\r\n" +
            "del /f /q \"%~f0\" > nul 2> nul\r\n");

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c {Quote(batchPath)}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static void RunAndWait(string fileName, string arguments, string workingDirectory, int timeoutMs)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        process?.WaitForExit(timeoutMs);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static void TryDeleteDirectoryIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static void TryDeleteRegistrySubKey(RegistryKey root, string path)
    {
        try
        {
            root.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static string Quote(string text) => "\"" + text.Replace("\"", "\\\"") + "\"";
}

internal sealed record UninstallOptions(
    bool Silent,
    string? InstallDirectory,
    string? FinalDeletePath,
    bool CleanupSelf)
{
    public static UninstallOptions Parse(string[] args)
    {
        var silent = false;
        string? installDirectory = null;
        string? finalDeletePath = null;
        var cleanupSelf = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase))
            {
                silent = true;
            }
            else if (string.Equals(arg, "--install-dir", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                installDirectory = args[++index];
            }
            else if (string.Equals(arg, "--final-delete", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                finalDeletePath = args[++index];
            }
            else if (string.Equals(arg, "--cleanup-self", StringComparison.OrdinalIgnoreCase))
            {
                cleanupSelf = true;
            }
        }

        return new UninstallOptions(silent, installDirectory, finalDeletePath, cleanupSelf);
    }
}

internal static class PathGuard
{
    public static bool IsSafeInstallDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(fullPath) || Path.GetPathRoot(fullPath)?.TrimEnd('\\') == fullPath)
        {
            return false;
        }

        var blocked = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        return blocked
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .All(value => !string.Equals(fullPath, value, StringComparison.OrdinalIgnoreCase));
    }
}
