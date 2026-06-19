namespace SystrayWrapDoubler;

internal sealed record AppCommand(bool Apply, bool Reset, bool RestartShell, bool RestorePromotionState, bool CleanupTemp)
{
    public static AppCommand? Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        var apply = false;
        var reset = false;
        var restartShell = false;
        var restorePromotionState = false;
        var cleanupTemp = false;

        foreach (var arg in args)
        {
            if (string.Equals(arg, "--apply", StringComparison.OrdinalIgnoreCase))
            {
                apply = true;
            }
            else if (string.Equals(arg, "--reset", StringComparison.OrdinalIgnoreCase))
            {
                reset = true;
            }
            else if (string.Equals(arg, "--restart-shell", StringComparison.OrdinalIgnoreCase))
            {
                restartShell = true;
            }
            else if (string.Equals(arg, "--restore-promotion-state", StringComparison.OrdinalIgnoreCase))
            {
                restorePromotionState = true;
            }
            else if (string.Equals(arg, "--cleanup-temp", StringComparison.OrdinalIgnoreCase))
            {
                cleanupTemp = true;
            }
        }

        if (!apply && !reset && !restartShell && !restorePromotionState && !cleanupTemp)
        {
            return null;
        }

        return new AppCommand(apply, reset, restartShell, restorePromotionState, cleanupTemp);
    }

    public int Execute()
    {
        try
        {
            if (Apply)
            {
                TrayOperations.ApplyDoubleRow();
            }

            if (Reset)
            {
                TrayOperations.RevertLayout();
            }

            if (RestorePromotionState)
            {
                PromotionStateStore.Restore();
            }

            if (CleanupTemp)
            {
                Cleanup.RemoveTempFiles();
            }

            if (RestartShell)
            {
                TrayOperations.RestartShell();
            }

            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Systray Wrap Doubler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
