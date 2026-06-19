namespace SystrayWrapDoubler;

internal sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MainForm _mainForm;
    private readonly SelfTrayPromotion _selfTrayPromotion;
    private readonly TaskbarIconGuard _taskbarIconGuard;

    public TrayAppContext()
    {
        _mainForm = new MainForm();
        _mainForm.FormClosed += (_, _) => ExitThread();

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIcon.Load(),
            Text = "Systray Wrap Doubler",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => ShowMainForm();
        _selfTrayPromotion = new SelfTrayPromotion();
        _taskbarIconGuard = new TaskbarIconGuard(RefreshTrayIcon);
        _selfTrayPromotion.Start();
        _mainForm.Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _selfTrayPromotion.Dispose();
            _taskbarIconGuard.Dispose();
            _mainForm.Dispose();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowMainForm());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Apply 2 Rows", null, (_, _) => RunTool(TrayOperations.ApplyDoubleRow, "Applied two-row tray layout."));
        menu.Items.Add("Revert", null, (_, _) => RunTool(TrayOperations.RevertLayout, "Reverted live tray layout."));
        menu.Items.Add("Restart Shell", null, (_, _) => RunTool(TrayOperations.RestartShell, "Explorer shell restarted."));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        return menu;
    }

    private void ShowMainForm()
    {
        if (_mainForm.IsDisposed)
        {
            return;
        }

        _mainForm.Show();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
    }

    private void RunTool(Action action, string successMessage)
    {
        try
        {
            action();
            _mainForm.SetStatus(successMessage);
        }
        catch (Exception ex)
        {
            _mainForm.SetStatus(ex.Message);
            MessageBox.Show(ex.Message, "Systray Wrap Doubler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExitApplication()
    {
        _mainForm.RequestExit();
    }

    private void RefreshTrayIcon()
    {
        if (_mainForm.IsDisposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Visible = true;
        _selfTrayPromotion.Start();
        _mainForm.SetStatus("Explorer restarted. Tray icon restored.");
    }
}
