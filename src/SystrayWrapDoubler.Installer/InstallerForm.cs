using System.Diagnostics;

namespace SystrayWrapDoubler.Installer;

internal sealed class InstallerForm : Form
{
    private readonly TextBox _installPathTextBox;
    private readonly CheckBox _desktopShortcutCheckBox;
    private readonly CheckBox _launchCheckBox;
    private readonly Button _installButton;
    private readonly Label _statusLabel;

    public InstallerForm()
    {
        Text = "Systray Wrap Doubler Setup";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        Width = 640;
        Height = 380;
        MinimumSize = new Size(560, 340);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var title = new Label
        {
            Text = "Systray Wrap Doubler",
            Font = new Font(Font.FontFamily, 18, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 24)
        };

        var copy = new Label
        {
            Text = "Installs the two-row Windows system tray tool, its native hook, documentation, and a full source copy so it can be studied or improved later.",
            AutoSize = false,
            Location = new Point(26, 70),
            Size = new Size(560, 58)
        };

        var pathLabel = new Label
        {
            Text = "Install location",
            AutoSize = true,
            Location = new Point(28, 142)
        };

        _installPathTextBox = new TextBox
        {
            Text = DefaultInstallPath(),
            Location = new Point(28, 166),
            Size = new Size(456, 27)
        };

        var browseButton = new Button
        {
            Text = "Browse",
            Location = new Point(494, 164),
            Size = new Size(92, 30)
        };
        browseButton.Click += (_, _) => BrowseInstallPath();

        _desktopShortcutCheckBox = new CheckBox
        {
            Text = "Create desktop shortcut",
            Checked = true,
            AutoSize = true,
            Location = new Point(30, 210)
        };

        _launchCheckBox = new CheckBox
        {
            Text = "Launch Systray Wrap Doubler after install",
            Checked = true,
            AutoSize = true,
            Location = new Point(30, 238)
        };

        _installButton = new Button
        {
            Text = "Install",
            Location = new Point(386, 286),
            Size = new Size(96, 34)
        };
        _installButton.Click += (_, _) => Install();

        var cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(492, 286),
            Size = new Size(96, 34)
        };
        cancelButton.Click += (_, _) => Close();

        _statusLabel = new Label
        {
            Text = "Ready.",
            AutoSize = false,
            Location = new Point(28, 286),
            Size = new Size(340, 34),
            TextAlign = ContentAlignment.MiddleLeft
        };

        Controls.Add(title);
        Controls.Add(copy);
        Controls.Add(pathLabel);
        Controls.Add(_installPathTextBox);
        Controls.Add(browseButton);
        Controls.Add(_desktopShortcutCheckBox);
        Controls.Add(_launchCheckBox);
        Controls.Add(_installButton);
        Controls.Add(cancelButton);
        Controls.Add(_statusLabel);
    }

    private void BrowseInstallPath()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where Systray Wrap Doubler should be installed",
            UseDescriptionForTitle = true,
            SelectedPath = _installPathTextBox.Text
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _installPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private void Install()
    {
        try
        {
            var installPath = Path.GetFullPath(_installPathTextBox.Text.Trim());
            if (!PathGuard.IsSafeInstallDirectory(installPath))
            {
                MessageBox.Show(
                    $"Please choose a normal app folder, not a system/root/user folder:\n{installPath}",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true, "Installing...");
            Directory.CreateDirectory(installPath);
            PayloadExtractor.ExtractTo(installPath);

            var appPath = Path.Combine(installPath, "SystrayWrapDoubler.exe");
            var uninstallerPath = Path.Combine(installPath, "Uninstall.exe");
            ShortcutHelper.CreateStartMenuShortcuts(appPath, uninstallerPath);
            if (_desktopShortcutCheckBox.Checked)
            {
                ShortcutHelper.CreateDesktopShortcut(appPath);
            }

            InstallRegistry.Write(installPath, appPath, uninstallerPath);
            SetBusy(false, "Installed.");

            var launchAfterInstall = _launchCheckBox.Checked;
            if (!launchAfterInstall)
            {
                MessageBox.Show(
                    "Systray Wrap Doubler is installed.\n\nRight-click its tray icon for Apply 2 Rows, Revert, Restart Shell, and Exit.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            Close();

            if (launchAfterInstall)
            {
                Process.Start(new ProcessStartInfo(appPath)
                {
                    WorkingDirectory = installPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            SetBusy(false, "Install failed.");
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _installButton.Enabled = !busy;
        _installPathTextBox.Enabled = !busy;
        _desktopShortcutCheckBox.Enabled = !busy;
        _launchCheckBox.Enabled = !busy;
        _statusLabel.Text = status;
        UseWaitCursor = busy;
        Application.DoEvents();
    }

    private static string DefaultInstallPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Systray Wrap Doubler");
}
