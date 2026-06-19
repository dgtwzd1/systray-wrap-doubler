using Microsoft.Win32;

namespace SystrayWrapDoubler;

internal sealed class SelfTrayPromotion : IDisposable
{
    private const string NotifyIconSettingsPath = @"Control Panel\NotifyIconSettings";
    private readonly System.Windows.Forms.Timer _timer;
    private int _attempts;

    public SelfTrayPromotion()
    {
        _timer = new System.Windows.Forms.Timer
        {
            Interval = 2000
        };
        _timer.Tick += (_, _) => PromoteAndStopWhenReady();
    }

    public void Start()
    {
        _attempts = 0;
        PromoteSelf();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    private void PromoteAndStopWhenReady()
    {
        _attempts++;
        if (PromoteSelf() || _attempts >= 15)
        {
            _timer.Stop();
        }
    }

    private static bool PromoteSelf()
    {
        using var baseKey = Registry.CurrentUser.OpenSubKey(NotifyIconSettingsPath, writable: true);
        if (baseKey is null)
        {
            return false;
        }

        var currentExe = Path.GetFullPath(Application.ExecutablePath);
        var promoted = false;

        foreach (var subKeyName in baseKey.GetSubKeyNames())
        {
            using var subKey = baseKey.OpenSubKey(subKeyName, writable: true);
            if (subKey is null)
            {
                continue;
            }

            var executablePath = subKey.GetValue("ExecutablePath") as string;
            var initialTooltip = subKey.GetValue("InitialTooltip") as string;
            if (!MatchesSelf(executablePath, initialTooltip, currentExe))
            {
                continue;
            }

            subKey.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
            TouchKey(subKey);
            promoted = true;
        }

        return promoted;
    }

    private static bool MatchesSelf(string? executablePath, string? initialTooltip, string currentExe)
    {
        if (string.Equals(executablePath, currentExe, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(initialTooltip, "Systray Wrap Doubler", StringComparison.OrdinalIgnoreCase);
    }

    private static void TouchKey(RegistryKey subKey)
    {
        const string tempValueName = "_temp_systray_wrap_doubler_touch";
        subKey.SetValue(tempValueName, "", RegistryValueKind.String);
        subKey.DeleteValue(tempValueName, throwOnMissingValue: false);
    }
}
