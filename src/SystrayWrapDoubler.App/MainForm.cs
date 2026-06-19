using System.Diagnostics;

namespace SystrayWrapDoubler;

internal sealed class MainForm : Form
{
    private readonly Label _statusLabel;
    private bool _allowExit;
    private bool _isRunningTool;

    public MainForm()
    {
        Text = "Systray Wrap Doubler";
        Icon = AppIcon.Load();
        Width = 640;
        Height = 360;
        MinimumSize = new Size(520, 300);
        StartPosition = FormStartPosition.CenterScreen;

        var menu = new MenuStrip();
        menu.Items.Add(BuildFileMenu());
        menu.Items.Add(BuildToolsMenu());
        menu.Items.Add(BuildAboutMenu());
        MainMenuStrip = menu;
        Controls.Add(menu);

        var title = new Label
        {
            Text = "Systray Wrap Doubler",
            Font = new Font(Font.FontFamily, 18, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 56)
        };

        var copy = new Label
        {
            Text = "Gives Windows 11 system tray icons a compact two-row layout. Use the tray icon or Tools menu to apply, revert, or restart Explorer.",
            AutoSize = false,
            Location = new Point(26, 96),
            Size = new Size(560, 56)
        };

        var applyButton = new Button
        {
            Text = "Apply 2 Rows",
            Location = new Point(28, 168),
            Size = new Size(150, 36)
        };
        applyButton.Click += (_, _) => RunTool(TrayOperations.ApplyDoubleRow, "Applied two-row tray layout.");

        var revertButton = new Button
        {
            Text = "Revert",
            Location = new Point(194, 168),
            Size = new Size(120, 36)
        };
        revertButton.Click += (_, _) => RunTool(TrayOperations.RevertLayout, "Reverted live tray layout.");

        var restartButton = new Button
        {
            Text = "Restart Shell",
            Location = new Point(330, 168),
            Size = new Size(140, 36)
        };
        restartButton.Click += (_, _) => RunTool(TrayOperations.RestartShell, "Explorer shell restarted.");

        _statusLabel = new Label
        {
            Text = "Ready. Right-click the tray icon for quick commands.",
            AutoSize = false,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(28, 232),
            Size = new Size(560, 36),
            TextAlign = ContentAlignment.MiddleLeft
        };

        Controls.Add(title);
        Controls.Add(copy);
        Controls.Add(applyButton);
        Controls.Add(revertButton);
        Controls.Add(restartButton);
        Controls.Add(_statusLabel);
    }

    public void SetStatus(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(message));
            return;
        }

        _statusLabel.Text = message;
    }

    public void RequestExit()
    {
        if (_isRunningTool)
        {
            SetStatus("Finish the current operation before exiting.");
            return;
        }

        try
        {
            SetStatus("Reverting tray layout before exit...");
            TrayOperations.RevertLayout();
        }
        catch (Exception ex)
        {
            SetStatus("Exit blocked: revert failed.");
            MessageBox.Show(
                "Systray Wrap Doubler stayed open because it could not safely revert the tray layout first.\n\n" + ex.Message,
                "Systray Wrap Doubler",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _allowExit = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_isRunningTool)
        {
            e.Cancel = true;
            SetStatus("Finish the current operation before closing.");
            return;
        }

        if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            SetStatus("Still running in the system tray.");
            return;
        }

        base.OnFormClosing(e);
    }

    private ToolStripMenuItem BuildFileMenu()
    {
        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add("Open", null, (_, _) => Show());
        file.DropDownItems.Add("Close to Tray", null, (_, _) => Hide());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Exit", null, (_, _) => RequestExit());
        return file;
    }

    private ToolStripMenuItem BuildToolsMenu()
    {
        var tools = new ToolStripMenuItem("Tools");
        tools.DropDownItems.Add("Apply 2 Rows", null, (_, _) => RunTool(TrayOperations.ApplyDoubleRow, "Applied two-row tray layout."));
        tools.DropDownItems.Add("Revert", null, (_, _) => RunTool(TrayOperations.RevertLayout, "Reverted live tray layout."));
        tools.DropDownItems.Add("Restart Shell", null, (_, _) => RunTool(TrayOperations.RestartShell, "Explorer shell restarted."));
        return tools;
    }

    private ToolStripMenuItem BuildAboutMenu()
    {
        var about = new ToolStripMenuItem("About");
        about.DropDownItems.Add("About This App", null, (_, _) => ShowAbout());
        about.DropDownItems.Add("Open Installed Source Folder", null, (_, _) => OpenInstalledPath("source"));
        about.DropDownItems.Add("Open Documentation Folder", null, (_, _) => OpenInstalledPath("docs"));
        return about;
    }

    private void RunTool(Action action, string successMessage)
    {
        try
        {
            _isRunningTool = true;
            action();
            SetStatus(successMessage);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            MessageBox.Show(ex.Message, "Systray Wrap Doubler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isRunningTool = false;
        }
    }

    private void ShowAbout()
    {
        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        MessageBox.Show(
            "Systray Wrap Doubler is open source. Feel free to change it, improve it, or take it over.\n\n" +
            "This app was made by Al with help from ChatGPT and Codex. If you share improvements and want to give a shout-out, that would be lovely.\n\n" +
            $"Installed source code is included here:\n{Path.Combine(installDir, "source")}",
            "About Systray Wrap Doubler",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void OpenInstalledPath(string child)
    {
        var path = Path.Combine(AppContext.BaseDirectory, child);
        if (!Directory.Exists(path))
        {
            MessageBox.Show($"Folder not found:\n{path}", "Systray Wrap Doubler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
