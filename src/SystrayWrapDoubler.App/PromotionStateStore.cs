using Microsoft.Win32;

namespace SystrayWrapDoubler;

internal static class PromotionStateStore
{
    public static string StateDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystrayWrapDoubler");

    public static string StatePath => Path.Combine(StateDirectory, "promotion-state.tsv");

    public static void Restore()
    {
        if (!File.Exists(StatePath))
        {
            return;
        }

        using var baseKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", writable: true);
        if (baseKey is null)
        {
            return;
        }

        foreach (var line in File.ReadAllLines(StatePath))
        {
            var parts = line.Split('\t');
            if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            using var subKey = baseKey.OpenSubKey(parts[0], writable: true);
            if (subKey is null)
            {
                continue;
            }

            if (string.Equals(parts[1], "missing", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    subKey.DeleteValue("IsPromoted", throwOnMissingValue: false);
                }
                catch
                {
                    // Continue restoring other entries.
                }
            }
            else if (string.Equals(parts[1], "dword", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(parts[2], out var value))
            {
                subKey.SetValue("IsPromoted", value, RegistryValueKind.DWord);
            }
        }
    }
}
