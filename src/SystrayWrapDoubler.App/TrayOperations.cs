using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SystrayWrapDoubler;

internal static class TrayOperations
{
    private static readonly Guid TrayWrapDoublerTapClsid = new("6F1DC928-7C3D-4A9E-A258-E5538FA83FA2");

    public static void ApplyDoubleRow()
    {
        var statePath = PromotionStateStore.StatePath;
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        Attach(reset: false, $"mode=double;rows=2;width=24;arrangement=columnFirstTopToBottom;state={statePath}");
    }

    public static void RevertLayout()
    {
        try
        {
            TryAttachResetWithShellRecovery();
        }
        finally
        {
            PromotionStateStore.Restore();
        }
    }

    public static void RestartShell()
    {
        var target = ExplorerTarget.FindPrimaryTaskbar();
        if (target is not null)
        {
            using var process = Process.GetProcessById(target.ProcessId);
            process.Kill();
            process.WaitForExit(5000);
        }

        Thread.Sleep(1500);
        if (ExplorerTarget.FindPrimaryTaskbar() is null)
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
    }

    private static void Attach(bool reset, string initData)
    {
        var target = ExplorerTarget.FindPrimaryTaskbar()
            ?? throw new InvalidOperationException("Could not find the primary explorer.exe taskbar target.");

        var tapDll = Path.Combine(AppContext.BaseDirectory, "TrayHook.Native.dll");
        if (!File.Exists(tapDll))
        {
            throw new FileNotFoundException("TrayHook.Native.dll was not found beside the app.", tapDll);
        }

        if (!string.Equals(target.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to target non-explorer process '{target.ProcessName}'.");
        }

        var lastHr = 0;
        for (var attempt = 1; attempt <= 256; attempt++)
        {
            lastHr = InitializeXamlDiagnosticsEx(
                $"VisualDiagConnection{attempt}",
                (uint)target.ProcessId,
                null,
                tapDll,
                TrayWrapDoublerTapClsid,
                initData);

            if (lastHr >= 0)
            {
                return;
            }

            if (!IsRetryable(lastHr))
            {
                break;
            }

            Thread.Sleep(reset ? 100 : 250);
        }

        throw new Win32Exception(lastHr, $"InitializeXamlDiagnosticsEx failed: 0x{lastHr:X8}");
    }

    private static void TryAttachResetWithShellRecovery()
    {
        try
        {
            Attach(reset: true, "mode=reset;rows=1;width=32;arrangement=normal");
        }
        catch
        {
            RestartShell();
            Attach(reset: true, "mode=reset;rows=1;width=32;arrangement=normal");
        }
    }

    private static bool IsRetryable(int hr) =>
        hr is unchecked((int)0x80070490) or unchecked((int)0x800401E3) or unchecked((int)0x800700AA);

    [DllImport("Windows.UI.Xaml.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int InitializeXamlDiagnosticsEx(
        string endPointName,
        uint pid,
        string? wszDllXamlDiagnostics,
        string wszTAPDllName,
        Guid tapClsid,
        string? wszInitializationData);

    private sealed class ExplorerTarget
    {
        public int ProcessId { get; init; }
        public string ProcessName { get; init; } = "";

        public static ExplorerTarget? FindPrimaryTaskbar()
        {
            IntPtr taskbarHandle = IntPtr.Zero;
            EnumWindows((handle, _) =>
            {
                if (!string.Equals(GetClassNameText(handle), "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                taskbarHandle = handle;
                return false;
            }, IntPtr.Zero);

            if (taskbarHandle == IntPtr.Zero)
            {
                return null;
            }

            GetWindowThreadProcessId(taskbarHandle, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                return new ExplorerTarget
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName
                };
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static string GetClassNameText(IntPtr handle)
        {
            var builder = new StringBuilder(512);
            var length = GetClassName(handle, builder, builder.Capacity);
            return length <= 0 ? "" : builder.ToString();
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    }
}
