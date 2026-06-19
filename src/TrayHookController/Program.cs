using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

var options = ControllerOptions.Parse(args);
if (options.ShowHelp)
{
    ControllerOptions.PrintHelp();
    return 0;
}

var target = options.ProcessId is not null
    ? ExplorerTarget.FromProcessId(options.ProcessId.Value)
    : ExplorerTarget.FindPrimaryTaskbar();

if (target is null)
{
    Console.Error.WriteLine("Could not find the primary explorer.exe taskbar target.");
    return 2;
}

var tapDll = options.TapDllPath is null
    ? Path.GetFullPath(Path.Combine(
        Environment.CurrentDirectory,
        "src",
        "TrayHook.Native",
        "x64",
        "Release",
        "TrayHook.Native.dll"))
    : Path.GetFullPath(options.TapDllPath);

var initData = options.Reset
    ? "mode=reset;rows=1;width=32;arrangement=normal"
    : $"mode=double;rows={options.Rows};width={options.Width};arrangement=columnFirstTopToBottom";

if (options.DumpTree)
{
    initData += ";dump=1";
}

Console.WriteLine("Systray Wrap Doubler hook controller");
Console.WriteLine($"Target: explorer.exe PID {target.ProcessId}, thread {target.ThreadId}, HWND {target.WindowHandle}");
Console.WriteLine($"TAP DLL: {tapDll}");
Console.WriteLine($"Settings: {initData}");
Console.WriteLine($"Mode: {(options.Apply ? "APPLY" : "dry-run")}");

if (!options.Apply)
{
    Console.WriteLine();
    Console.WriteLine("Dry-run only. Pass --apply to call InitializeXamlDiagnosticsEx and load the TAP DLL into explorer.exe.");
    return 0;
}

if (!File.Exists(tapDll))
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("The TAP DLL does not exist yet. Build src\\TrayHook.Native first, then rerun with --apply.");
    return 3;
}

if (!string.Equals(target.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Refusing to target non-explorer process '{target.ProcessName}'.");
    return 4;
}

var attachResult = XamlDiagnosticsAttacher.Attach(
    (uint)target.ProcessId,
    tapDll,
    initData,
    options.Attempts,
    options.DelayMs);

if (!attachResult.Success)
{
    Console.Error.WriteLine($"InitializeXamlDiagnosticsEx failed after {attachResult.AttemptsUsed} attempt(s): 0x{attachResult.HResult:X8}");
    return 5;
}

Console.WriteLine($"InitializeXamlDiagnosticsEx returned success on {attachResult.EndpointName}.");
Console.WriteLine("Check %TEMP%\\SystrayWrapDoubler.Native.log for TAP-side mutation details.");
return 0;

internal sealed record AttachResult(bool Success, int HResult, string EndpointName, int AttemptsUsed);

internal static class XamlDiagnosticsAttacher
{
    private const int ElementNotFound = unchecked((int)0x80070490);
    private const int OperationUnavailable = unchecked((int)0x800401E3);
    private const int ResourceInUse = unchecked((int)0x800700AA);

    public static AttachResult Attach(uint processId, string tapDll, string initData, int attempts, int delayMs)
    {
        var lastHr = 0;
        var lastEndpoint = "";

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var endpointName = $"VisualDiagConnection{attempt}";
            Console.Write($"Attach attempt {attempt}/{attempts} ({endpointName})... ");

            lastHr = NativeMethods.InitializeXamlDiagnosticsEx(
                endpointName,
                processId,
                null,
                tapDll,
                NativeMethods.TrayWrapDoublerTapClsid,
                initData);
            lastEndpoint = endpointName;

            if (lastHr >= 0)
            {
                Console.WriteLine("success");
                return new AttachResult(true, lastHr, endpointName, attempt);
            }

            Console.WriteLine($"failed 0x{lastHr:X8}");
            if (!IsRetryable(lastHr))
            {
                break;
            }

            if (attempt < attempts && delayMs > 0)
            {
                Thread.Sleep(delayMs);
            }
        }

        return new AttachResult(false, lastHr, lastEndpoint, attempts);
    }

    private static bool IsRetryable(int hr) =>
        hr is ElementNotFound or OperationUnavailable or ResourceInUse;
}

internal sealed record ControllerOptions
{
    public bool ShowHelp { get; init; }
    public bool Apply { get; init; }
    public int? ProcessId { get; init; }
    public string? TapDllPath { get; init; }
    public bool Reset { get; init; }
    public bool DumpTree { get; init; }
    public int Rows { get; init; } = 2;
    public int Width { get; init; } = 24;
    public int Attempts { get; init; } = 64;
    public int DelayMs { get; init; } = 150;

    public static ControllerOptions Parse(string[] args)
    {
        var options = new ControllerOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "-h" or "--help" or "/?")
            {
                options = options with { ShowHelp = true };
            }
            else if (string.Equals(arg, "--apply", StringComparison.OrdinalIgnoreCase))
            {
                options = options with { Apply = true };
            }
            else if (string.Equals(arg, "--reset", StringComparison.OrdinalIgnoreCase))
            {
                options = options with { Reset = true };
            }
            else if (string.Equals(arg, "--dump-tree", StringComparison.OrdinalIgnoreCase))
            {
                options = options with { DumpTree = true };
            }
            else if (string.Equals(arg, "--pid", StringComparison.OrdinalIgnoreCase) &&
                     index + 1 < args.Length &&
                     int.TryParse(args[++index], out var pid))
            {
                options = options with { ProcessId = pid };
            }
            else if (string.Equals(arg, "--dll", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options = options with { TapDllPath = args[++index] };
            }
            else if (string.Equals(arg, "--rows", StringComparison.OrdinalIgnoreCase) &&
                     index + 1 < args.Length &&
                     int.TryParse(args[++index], out var rows))
            {
                options = options with { Rows = Math.Clamp(rows, 1, 4) };
            }
            else if (string.Equals(arg, "--width", StringComparison.OrdinalIgnoreCase) &&
                     index + 1 < args.Length &&
                     int.TryParse(args[++index], out var width))
            {
                options = options with { Width = Math.Clamp(width, 16, 48) };
            }
            else if (string.Equals(arg, "--attempts", StringComparison.OrdinalIgnoreCase) &&
                     index + 1 < args.Length &&
                     int.TryParse(args[++index], out var attempts))
            {
                options = options with { Attempts = Math.Clamp(attempts, 1, 256) };
            }
            else if (string.Equals(arg, "--delay-ms", StringComparison.OrdinalIgnoreCase) &&
                     index + 1 < args.Length &&
                     int.TryParse(args[++index], out var delayMs))
            {
                options = options with { DelayMs = Math.Clamp(delayMs, 0, 5000) };
            }
        }

        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("TrayHookController");
        Console.WriteLine();
        Console.WriteLine("Dry-run:");
        Console.WriteLine("  dotnet run --project src\\TrayHookController\\TrayHookController.csproj");
        Console.WriteLine();
        Console.WriteLine("Apply:");
        Console.WriteLine("  dotnet run --project src\\TrayHookController\\TrayHookController.csproj -- --apply --dll <path-to-TrayHook.Native.dll>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --apply          Load the TAP DLL into explorer.exe through InitializeXamlDiagnosticsEx.");
        Console.WriteLine("  --reset          Clear the tray layout mutation instead of applying the two-row layout.");
        Console.WriteLine("  --dump-tree      Write one focused SystemTrayFrameGrid XAML tree dump to the native log.");
        Console.WriteLine("  --dll <path>     Native TAP DLL path.");
        Console.WriteLine("  --pid <pid>      Override target process. Must still be explorer.exe.");
        Console.WriteLine("  --rows <n>       Tray rows, default 2.");
        Console.WriteLine("  --width <px>     Tray icon slot width, default 24.");
        Console.WriteLine("  --attempts <n>   XAML diagnostics endpoint attempts, default 64.");
        Console.WriteLine("  --delay-ms <n>   Delay between retryable attach attempts, default 150.");
    }

}

internal sealed class ExplorerTarget
{
    public int ProcessId { get; init; }
    public int ThreadId { get; init; }
    public string ProcessName { get; init; } = "";
    public string WindowHandle { get; init; } = "";

    public static ExplorerTarget? FindPrimaryTaskbar()
    {
        IntPtr taskbarHandle = IntPtr.Zero;
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!string.Equals(NativeMethods.GetClassNameText(handle), "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            taskbarHandle = handle;
            return false;
        }, IntPtr.Zero);

        return taskbarHandle == IntPtr.Zero ? null : FromWindow(taskbarHandle);
    }

    public static ExplorerTarget? FromProcessId(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new ExplorerTarget
            {
                ProcessId = process.Id,
                ThreadId = 0,
                ProcessName = process.ProcessName,
                WindowHandle = "<pid override>"
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static ExplorerTarget? FromWindow(IntPtr handle)
    {
        var threadId = NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return new ExplorerTarget
            {
                ProcessId = process.Id,
                ThreadId = (int)threadId,
                ProcessName = process.ProcessName,
                WindowHandle = NativeMethods.FormatHandle(handle)
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

internal static partial class NativeMethods
{
    public static readonly Guid TrayWrapDoublerTapClsid = new("6F1DC928-7C3D-4A9E-A258-E5538FA83FA2");

    public static string FormatHandle(IntPtr handle) => $"0x{handle.ToInt64():X}";

    public static string GetClassNameText(IntPtr handle)
    {
        var builder = new StringBuilder(512);
        var length = GetClassName(handle, builder, builder.Capacity);
        return length <= 0 ? "" : builder.ToString();
    }

    [DllImport("Windows.UI.Xaml.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int InitializeXamlDiagnosticsEx(
        string endPointName,
        uint pid,
        string? wszDllXamlDiagnostics,
        string wszTAPDllName,
        Guid tapClsid,
        string? wszInitializationData);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
}
