namespace SystrayWrapDoubler.Installer;

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
