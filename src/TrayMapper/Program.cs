using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var options = MapperOptions.Parse(args);
var mapper = new TaskbarMapper(options);
var report = mapper.Capture();

var outputPath = options.OutputPath ?? Path.Combine(
    Environment.CurrentDirectory,
    "artifacts",
    $"tray-map-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");

outputPath = Path.GetFullPath(outputPath);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

File.WriteAllText(outputPath, JsonSerializer.Serialize(report, jsonOptions));

Console.WriteLine("TrayMapper read-only capture complete.");
Console.WriteLine($"Report: {outputPath}");
Console.WriteLine();

foreach (var root in report.Roots)
{
    Console.WriteLine($"{root.Kind}: {root.Window.ClassName} {root.Window.Handle} {root.Window.WindowRect}");

    foreach (var toolbar in root.Toolbars)
    {
        var summary = toolbar.ToolbarSummary;
        if (summary is null)
        {
            Console.WriteLine($"  Toolbar {toolbar.Handle}: no summary");
            continue;
        }

        Console.WriteLine(
            $"  Toolbar {toolbar.Handle}: buttons={summary.ButtonCount}, " +
            $"visible={summary.VisibleButtonCount}, rows={summary.RowCount}, " +
            $"slot={summary.EstimatedSlotWidth}x{summary.EstimatedSlotHeight}, " +
            $"read={summary.ReadStatus}");
    }

    if (root.InterestingUiaElements.Count > 0)
    {
        if (root.TrayLayoutAnalysis is not null && root.TrayLayoutAnalysis.TrayButtonCount > 0)
        {
            var analysis = root.TrayLayoutAnalysis;
            Console.WriteLine(
                $"  Tray UIA layout: buttons={analysis.TrayButtonCount}, notifyIcons={analysis.NotifyIconCount}, " +
                $"systemIcons={analysis.SystemIconCount}, rows={analysis.RowCount}, " +
                $"slot={analysis.EstimatedSlotWidth}x{analysis.EstimatedSlotHeight}, " +
                $"span={analysis.SpanWidth}px, twoRowEstimate={analysis.TwoRowWidthAtCurrentSlot}px");
        }

        foreach (var plan in root.TrayLayoutPlans)
        {
            Console.WriteLine(
                $"  Plan '{plan.Name}': candidates={plan.PlannedButtonCount}, excluded={plan.ExcludedButtonCount}, " +
                $"rows={plan.RequestedRows}, columns={plan.ColumnCount}, width={plan.ProposedBoundingRect.Width}px, " +
                $"saved={plan.EstimatedWidthSaved}px, rowHeight={plan.RowHeight}px");
        }

        Console.WriteLine("  UIA interesting elements:");
        foreach (var element in root.InterestingUiaElements.Take(24))
        {
            Console.WriteLine(
                $"    {element.ControlTypeName} id='{element.AutomationId}' name='{element.Name}' " +
                $"class='{element.ClassName}' rect={element.BoundingRectangle}");
        }

        if (root.InterestingUiaElements.Count > 24)
        {
            Console.WriteLine($"    ... {root.InterestingUiaElements.Count - 24} more");
        }
    }
}

if (report.Roots.Count == 0)
{
    Console.WriteLine("No taskbar or notification-area roots were found.");
}

if (report.HookTargets is not null)
{
    Console.WriteLine();
    Console.WriteLine("Hook target finder:");
    Console.WriteLine($"- safety: {report.HookTargets.SafetyBoundary}");
    Console.WriteLine($"- explorer processes: {report.HookTargets.ExplorerProcesses.Count}");
    Console.WriteLine($"- taskbar windows: {report.HookTargets.TaskbarWindows.Count}");

    foreach (var rootTarget in report.HookTargets.UiaRoots.Where(root => root.CommonTrayAncestor is not null))
    {
        Console.WriteLine(
            $"- {rootTarget.RootKind}: tray buttons={rootTarget.TrayButtonCount}, " +
            $"movable={rootTarget.MovableTrayButtonCount}, " +
            $"movable ancestor='{rootTarget.MovableTrayAncestor?.ClassName ?? rootTarget.CommonTrayAncestor!.ClassName}' " +
            $"id='{rootTarget.MovableTrayAncestor?.AutomationId ?? rootTarget.CommonTrayAncestor!.AutomationId}' " +
            $"depth={rootTarget.MovableTrayAncestor?.Depth ?? rootTarget.CommonTrayAncestor!.Depth}");
    }

    foreach (var finding in report.HookTargets.Findings)
    {
        Console.WriteLine($"  {finding}");
    }
}

if (report.Findings.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Findings:");
    foreach (var finding in report.Findings)
    {
        Console.WriteLine($"- {finding}");
    }
}

internal sealed class TaskbarMapper(MapperOptions options)
{
    private static readonly HashSet<string> RootClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow"
    };

    private static readonly string[] DiscoveryClassFragments =
    [
        "Tray",
        "Notify",
        "Overflow"
    ];

    public CaptureReport Capture()
    {
        var topLevelWindows = NativeMethods.GetTopLevelWindows()
            .Select(WindowSnapshot.FromHandle)
            .Where(window => window is not null)
            .Cast<WindowSnapshot>()
            .ToList();

        var rootHandles = topLevelWindows
            .Where(IsRootCandidate)
            .Select(window => window.RawHandle)
            .Distinct()
            .ToList();

        var roots = new List<RootSnapshot>();
        foreach (var handle in rootHandles)
        {
            var root = CaptureRoot(handle);
            roots.Add(root);
        }

        var hookTargets = HookTargetFinder.Capture(roots);

        var report = new CaptureReport
        {
            CapturedAt = DateTimeOffset.Now,
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            OsVersion = Environment.OSVersion.VersionString,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Roots = roots,
            HookTargets = hookTargets
        };

        AddFindings(report);
        return report;
    }

    private bool IsRootCandidate(WindowSnapshot window)
    {
        if (RootClasses.Contains(window.ClassName))
        {
            return true;
        }

        if (!options.IncludeDiscoveryRoots)
        {
            return false;
        }

        if (!string.Equals(window.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return DiscoveryClassFragments.Any(fragment =>
            window.ClassName.Contains(fragment, StringComparison.OrdinalIgnoreCase) ||
            window.Text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private RootSnapshot CaptureRoot(IntPtr handle)
    {
        var window = CaptureWindowTree(handle, 0);
        var kind = window.ClassName switch
        {
            "Shell_TrayWnd" => "Primary taskbar",
            "Shell_SecondaryTrayWnd" => "Secondary taskbar",
            "NotifyIconOverflowWindow" => "Tray overflow",
            _ => "Discovery root"
        };

        var uiaRoot = UiaReader.TryCapture(handle, options.MaxUiaDepth, options.MaxUiaElements, out var uiaStatus);
        var interestingUiaElements = uiaRoot is null
            ? []
            : UiaReader.FindInterestingElements(uiaRoot).ToList();
        var trayLayoutAnalysis = UiaReader.AnalyzeTrayLayout(interestingUiaElements);

        return new RootSnapshot
        {
            Kind = kind,
            Window = window,
            Toolbars = Flatten(window)
                .Where(node => string.Equals(node.ClassName, "ToolbarWindow32", StringComparison.OrdinalIgnoreCase))
                .ToList(),
            UiaStatus = uiaStatus,
            UiaRoot = uiaRoot,
            InterestingUiaElements = interestingUiaElements,
            TrayLayoutAnalysis = trayLayoutAnalysis,
            TrayLayoutPlans = UiaReader.CreateLayoutPlans(interestingUiaElements, trayLayoutAnalysis)
        };
    }

    private WindowSnapshot CaptureWindowTree(IntPtr handle, int depth)
    {
        var snapshot = WindowSnapshot.FromHandle(handle) ?? WindowSnapshot.Unknown(handle);

        if (string.Equals(snapshot.ClassName, "ToolbarWindow32", StringComparison.OrdinalIgnoreCase))
        {
            var toolbarRead = ToolbarReader.TryRead(handle);
            snapshot.ToolbarSummary = toolbarRead.Summary;
            snapshot.ToolbarButtons.AddRange(toolbarRead.Buttons);
        }

        if (depth >= options.MaxDepth)
        {
            snapshot.ChildrenTruncated = true;
            return snapshot;
        }

        foreach (var childHandle in NativeMethods.GetDirectChildWindows(handle))
        {
            snapshot.Children.Add(CaptureWindowTree(childHandle, depth + 1));
        }

        return snapshot;
    }

    private static IEnumerable<WindowSnapshot> Flatten(WindowSnapshot root)
    {
        yield return root;

        foreach (var child in root.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private static void AddFindings(CaptureReport report)
    {
        var allToolbars = report.Roots.SelectMany(root => root.Toolbars).ToList();
        var interestingUiaElements = report.Roots.SelectMany(root => root.InterestingUiaElements).ToList();

        if (allToolbars.Count == 0)
        {
            report.Findings.Add("No classic ToolbarWindow32 tray containers were found under the detected taskbar roots.");
            if (interestingUiaElements.Count == 0)
            {
                report.Findings.Add("UI Automation did not expose named tray elements in the captured roots.");
                report.Findings.Add("A layout utility would likely need an Explorer hook rather than ordinary HWND/UIA inspection.");
            }
            else
            {
                report.Findings.Add($"UI Automation exposed {interestingUiaElements.Count} tray/taskbar-related elements.");
                report.Findings.Add("That is useful for mapping and diagnostics, but UI Automation normally cannot reposition Explorer-owned XAML layout.");
            }

            return;
        }

        var readable = allToolbars.Where(toolbar => toolbar.ToolbarSummary?.CanReadButtons == true).ToList();
        if (readable.Count == 0)
        {
            report.Findings.Add("Classic toolbar containers were found, but button rectangles could not be read.");
            report.Findings.Add("A layout utility would likely need higher-risk Explorer internals instead of ordinary HWND inspection.");
            return;
        }

        foreach (var toolbar in readable)
        {
            var summary = toolbar.ToolbarSummary!;
            if (summary.RowCount <= 1 && summary.VisibleButtonCount > 1)
            {
                report.Findings.Add(
                    $"Toolbar {toolbar.Handle} exposes {summary.VisibleButtonCount} visible buttons in one row. " +
                    "This is enough data for a non-mutating layout analysis pass.");
            }

            if (summary.RowCount > 1)
            {
                report.Findings.Add(
                    $"Toolbar {toolbar.Handle} already reports {summary.RowCount} rows at the HWND toolbar level.");
            }
        }
    }
}

internal sealed class MapperOptions
{
    public int MaxDepth { get; private init; } = 12;
    public int MaxUiaDepth { get; private init; } = 12;
    public int MaxUiaElements { get; private init; } = 1500;
    public bool IncludeDiscoveryRoots { get; private init; } = true;
    public string? OutputPath { get; private init; }

    public static MapperOptions Parse(string[] args)
    {
        var options = new MapperOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--out", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                options = options.WithOutputPath(args[++index]);
            }
            else if (string.Equals(arg, "--max-depth", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length &&
                     int.TryParse(args[++index], out var maxDepth))
            {
                options = options.WithMaxDepth(Math.Clamp(maxDepth, 1, 32));
            }
            else if (string.Equals(arg, "--max-uia-depth", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length &&
                     int.TryParse(args[++index], out var maxUiaDepth))
            {
                options = options.WithMaxUiaDepth(Math.Clamp(maxUiaDepth, 1, 32));
            }
            else if (string.Equals(arg, "--max-uia-elements", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length &&
                     int.TryParse(args[++index], out var maxUiaElements))
            {
                options = options.WithMaxUiaElements(Math.Clamp(maxUiaElements, 50, 10000));
            }
            else if (string.Equals(arg, "--no-discovery-roots", StringComparison.OrdinalIgnoreCase))
            {
                options = options.WithDiscoveryRoots(false);
            }
        }

        return options;
    }

    private MapperOptions WithOutputPath(string outputPath) => new()
    {
        MaxDepth = MaxDepth,
        MaxUiaDepth = MaxUiaDepth,
        MaxUiaElements = MaxUiaElements,
        IncludeDiscoveryRoots = IncludeDiscoveryRoots,
        OutputPath = outputPath
    };

    private MapperOptions WithMaxDepth(int maxDepth) => new()
    {
        MaxDepth = maxDepth,
        MaxUiaDepth = MaxUiaDepth,
        MaxUiaElements = MaxUiaElements,
        IncludeDiscoveryRoots = IncludeDiscoveryRoots,
        OutputPath = OutputPath
    };

    private MapperOptions WithMaxUiaDepth(int maxUiaDepth) => new()
    {
        MaxDepth = MaxDepth,
        MaxUiaDepth = maxUiaDepth,
        MaxUiaElements = MaxUiaElements,
        IncludeDiscoveryRoots = IncludeDiscoveryRoots,
        OutputPath = OutputPath
    };

    private MapperOptions WithMaxUiaElements(int maxUiaElements) => new()
    {
        MaxDepth = MaxDepth,
        MaxUiaDepth = MaxUiaDepth,
        MaxUiaElements = maxUiaElements,
        IncludeDiscoveryRoots = IncludeDiscoveryRoots,
        OutputPath = OutputPath
    };

    private MapperOptions WithDiscoveryRoots(bool includeDiscoveryRoots) => new()
    {
        MaxDepth = MaxDepth,
        MaxUiaDepth = MaxUiaDepth,
        MaxUiaElements = MaxUiaElements,
        IncludeDiscoveryRoots = includeDiscoveryRoots,
        OutputPath = OutputPath
    };
}

internal sealed class CaptureReport
{
    public DateTimeOffset CapturedAt { get; init; }
    public string MachineName { get; init; } = "";
    public string UserName { get; init; } = "";
    public string OsVersion { get; init; } = "";
    public string ProcessArchitecture { get; init; } = "";
    public List<RootSnapshot> Roots { get; init; } = [];
    public HookTargetReport? HookTargets { get; init; }
    public List<string> Findings { get; } = [];
}

internal sealed class RootSnapshot
{
    public string Kind { get; init; } = "";
    public WindowSnapshot Window { get; init; } = WindowSnapshot.Unknown(IntPtr.Zero);
    public List<WindowSnapshot> Toolbars { get; init; } = [];
    public string UiaStatus { get; init; } = "";
    public UiaElementSnapshot? UiaRoot { get; init; }
    public List<UiaElementSnapshot> InterestingUiaElements { get; init; } = [];
    public TrayLayoutAnalysis? TrayLayoutAnalysis { get; init; }
    public List<TrayLayoutPlan> TrayLayoutPlans { get; init; } = [];
}

internal sealed class WindowSnapshot
{
    [JsonIgnore]
    public IntPtr RawHandle { get; private init; }

    public string Handle { get; private init; } = "";
    public string ClassName { get; private init; } = "";
    public string Text { get; private init; } = "";
    public int ProcessId { get; private init; }
    public int ThreadId { get; private init; }
    public string? ProcessName { get; private init; }
    public bool IsVisible { get; private init; }
    public bool IsEnabled { get; private init; }
    public RectSnapshot WindowRect { get; private init; } = RectSnapshot.Empty;
    public RectSnapshot ClientRectOnScreen { get; private init; } = RectSnapshot.Empty;
    public string StyleHex { get; private init; } = "";
    public string ExStyleHex { get; private init; } = "";
    public bool ChildrenTruncated { get; set; }
    public ToolbarSummary? ToolbarSummary { get; set; }
    public List<ToolbarButtonSnapshot> ToolbarButtons { get; } = [];
    public List<WindowSnapshot> Children { get; } = [];

    public static WindowSnapshot? FromHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle))
        {
            return null;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(handle, out var processId);

        return new WindowSnapshot
        {
            RawHandle = handle,
            Handle = NativeMethods.FormatHandle(handle),
            ClassName = NativeMethods.GetClassNameText(handle),
            Text = NativeMethods.GetWindowTextSafe(handle),
            ProcessId = (int)processId,
            ThreadId = (int)threadId,
            ProcessName = TryGetProcessName((int)processId),
            IsVisible = NativeMethods.IsWindowVisible(handle),
            IsEnabled = NativeMethods.IsWindowEnabled(handle),
            WindowRect = NativeMethods.GetWindowRectSnapshot(handle),
            ClientRectOnScreen = NativeMethods.GetClientRectOnScreenSnapshot(handle),
            StyleHex = NativeMethods.GetWindowLongPtrHex(handle, NativeMethods.GwlStyle),
            ExStyleHex = NativeMethods.GetWindowLongPtrHex(handle, NativeMethods.GwlExStyle)
        };
    }

    public static WindowSnapshot Unknown(IntPtr handle) => new()
    {
        RawHandle = handle,
        Handle = NativeMethods.FormatHandle(handle),
        ClassName = "<unknown>"
    };

    private static string? TryGetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class ToolbarReadResult
{
    public ToolbarSummary Summary { get; init; } = new();
    public List<ToolbarButtonSnapshot> Buttons { get; init; } = [];
}

internal sealed class ToolbarSummary
{
    public bool CanReadButtons { get; init; }
    public string ReadStatus { get; init; } = "";
    public int ButtonCount { get; init; }
    public int VisibleButtonCount { get; init; }
    public int HiddenButtonCount { get; init; }
    public int RowCount { get; init; }
    public int EstimatedSlotWidth { get; init; }
    public int EstimatedSlotHeight { get; init; }
}

internal sealed class ToolbarButtonSnapshot
{
    public int Index { get; init; }
    public int CommandId { get; init; }
    public string Text { get; init; } = "";
    public string StateHex { get; init; } = "";
    public string StyleHex { get; init; } = "";
    public bool IsEnabled { get; init; }
    public bool IsHidden { get; init; }
    public bool IsChecked { get; init; }
    public RectSnapshot RectClient { get; init; } = RectSnapshot.Empty;
    public RectSnapshot RectScreen { get; init; } = RectSnapshot.Empty;
}

internal readonly record struct RectSnapshot(int Left, int Top, int Right, int Bottom)
{
    public static RectSnapshot Empty { get; } = new(0, 0, 0, 0);
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public override string ToString() => $"{Left},{Top} {Width}x{Height}";
}

internal sealed class UiaElementSnapshot
{
    public string Name { get; init; } = "";
    public string AutomationId { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string FrameworkId { get; init; } = "";
    public string ControlTypeName { get; init; } = "";
    public int ProcessId { get; init; }
    public bool IsOffscreen { get; init; }
    public RectSnapshot BoundingRectangle { get; init; } = RectSnapshot.Empty;
    public List<UiaElementSnapshot> Children { get; } = [];
}

internal sealed class TrayLayoutAnalysis
{
    public int TrayButtonCount { get; init; }
    public int NotifyIconCount { get; init; }
    public int SystemIconCount { get; init; }
    public int RowCount { get; init; }
    public int EstimatedSlotWidth { get; init; }
    public int EstimatedSlotHeight { get; init; }
    public int SpanLeft { get; init; }
    public int SpanRight { get; init; }
    public int SpanWidth { get; init; }
    public int TwoRowWidthAtCurrentSlot { get; init; }
    public int TwoRowWidthAt24PxSlot { get; init; }
    public string Capability { get; init; } = "";
}

internal sealed class TrayLayoutPlan
{
    public string Name { get; init; } = "";
    public string Strategy { get; init; } = "";
    public int RequestedRows { get; init; }
    public int ColumnCount { get; init; }
    public int PlannedButtonCount { get; init; }
    public int ExcludedButtonCount { get; init; }
    public int RowHeight { get; init; }
    public int AnchorRight { get; init; }
    public RectSnapshot CurrentCandidateBoundingRect { get; init; } = RectSnapshot.Empty;
    public RectSnapshot ProposedBoundingRect { get; init; } = RectSnapshot.Empty;
    public int EstimatedWidthSaved { get; init; }
    public string WriteCapability { get; init; } = "";
    public List<TrayLayoutPlanItem> Items { get; init; } = [];
    public List<TrayLayoutExcludedItem> ExcludedItems { get; init; } = [];
    public List<string> Notes { get; init; } = [];
}

internal sealed class TrayLayoutPlanItem
{
    public int OriginalIndex { get; init; }
    public int Row { get; init; }
    public int Column { get; init; }
    public string Name { get; init; } = "";
    public string AutomationId { get; init; } = "";
    public string ClassName { get; init; } = "";
    public RectSnapshot CurrentRect { get; init; } = RectSnapshot.Empty;
    public RectSnapshot ProposedRect { get; init; } = RectSnapshot.Empty;
}

internal sealed class TrayLayoutExcludedItem
{
    public string Reason { get; init; } = "";
    public string Name { get; init; } = "";
    public string AutomationId { get; init; } = "";
    public string ClassName { get; init; } = "";
    public RectSnapshot CurrentRect { get; init; } = RectSnapshot.Empty;
}

internal sealed class HookTargetReport
{
    public bool IsReadOnly { get; init; } = true;
    public string SafetyBoundary { get; init; } =
        "Target finder only. No Explorer restart, injection, registry edit, global hook, or layout mutation is performed.";
    public int? PrimaryExplorerProcessId { get; init; }
    public int? PrimaryTaskbarThreadId { get; init; }
    public string PrimaryTaskbarHandle { get; init; } = "";
    public List<ExplorerProcessTarget> ExplorerProcesses { get; init; } = [];
    public List<TaskbarWindowTarget> TaskbarWindows { get; init; } = [];
    public List<UiaHookRootTarget> UiaRoots { get; init; } = [];
    public List<string> Findings { get; init; } = [];
}

internal sealed class ExplorerProcessTarget
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public string MainModulePath { get; init; } = "";
    public string MainWindowHandle { get; init; } = "";
    public string StartTime { get; init; } = "";
    public List<string> OwnedTaskbarWindowHandles { get; init; } = [];
    public List<int> OwnedTaskbarThreadIds { get; init; } = [];
    public string ModuleStatus { get; init; } = "";
    public List<ProcessModuleTarget> InterestingModules { get; init; } = [];
}

internal sealed class ProcessModuleTarget
{
    public string Name { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string BaseAddress { get; init; } = "";
    public int MemorySize { get; init; }
}

internal sealed class TaskbarWindowTarget
{
    public string Kind { get; init; } = "";
    public string Handle { get; init; } = "";
    public string ClassName { get; init; } = "";
    public int ProcessId { get; init; }
    public int ThreadId { get; init; }
    public RectSnapshot WindowRect { get; init; } = RectSnapshot.Empty;
    public string UiaStatus { get; init; } = "";
    public int TrayButtonCount { get; init; }
}

internal sealed class UiaHookRootTarget
{
    public string RootKind { get; init; } = "";
    public string RootHandle { get; init; } = "";
    public int ProcessId { get; init; }
    public int ThreadId { get; init; }
    public int TrayButtonCount { get; init; }
    public int MovableTrayButtonCount { get; init; }
    public UiaHookElementTarget? CommonTrayAncestor { get; init; }
    public UiaHookElementTarget? MovableTrayAncestor { get; init; }
    public List<UiaHookElementTarget> TrayButtons { get; init; } = [];
    public List<UiaHookElementTarget> FixedRightItems { get; init; } = [];
    public List<string> Notes { get; init; } = [];
}

internal sealed class UiaHookElementTarget
{
    public string Reason { get; init; } = "";
    public int Depth { get; init; }
    public string Name { get; init; } = "";
    public string AutomationId { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string FrameworkId { get; init; } = "";
    public string ControlTypeName { get; init; } = "";
    public int ProcessId { get; init; }
    public bool IsOffscreen { get; init; }
    public int DirectChildCount { get; init; }
    public RectSnapshot BoundingRectangle { get; init; } = RectSnapshot.Empty;
    public List<UiaPathSegment> Path { get; init; } = [];
}

internal sealed class UiaPathSegment
{
    public int Depth { get; init; }
    public string Name { get; init; } = "";
    public string AutomationId { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string ControlTypeName { get; init; } = "";
    public RectSnapshot BoundingRectangle { get; init; } = RectSnapshot.Empty;
}

internal static class HookTargetFinder
{
    private static readonly string[] InterestingModuleFragments =
    [
        "xaml",
        "taskbar",
        "twinui",
        "shell",
        "notification",
        "immersive",
        "uxtheme",
        "comctl32",
        "coremessaging",
        "coreui"
    ];

    public static HookTargetReport Capture(IReadOnlyCollection<RootSnapshot> roots)
    {
        var taskbarWindows = roots
            .Select(root => new TaskbarWindowTarget
            {
                Kind = root.Kind,
                Handle = root.Window.Handle,
                ClassName = root.Window.ClassName,
                ProcessId = root.Window.ProcessId,
                ThreadId = root.Window.ThreadId,
                WindowRect = root.Window.WindowRect,
                UiaStatus = root.UiaStatus,
                TrayButtonCount = root.TrayLayoutAnalysis?.TrayButtonCount ?? 0
            })
            .ToList();

        var uiaRoots = roots
            .Where(root => root.UiaRoot is not null)
            .Select(CaptureUiaRootTarget)
            .ToList();

        var explorerProcesses = new List<ExplorerProcessTarget>();
        foreach (var process in Process.GetProcessesByName("explorer").OrderBy(process => process.Id))
        {
            try
            {
                explorerProcesses.Add(CaptureExplorerProcess(process, taskbarWindows));
            }
            finally
            {
                process.Dispose();
            }
        }

        var primaryTaskbar = taskbarWindows.FirstOrDefault(window =>
            string.Equals(window.Kind, "Primary taskbar", StringComparison.OrdinalIgnoreCase));

        var report = new HookTargetReport
        {
            PrimaryExplorerProcessId = primaryTaskbar?.ProcessId,
            PrimaryTaskbarThreadId = primaryTaskbar?.ThreadId,
            PrimaryTaskbarHandle = primaryTaskbar?.Handle ?? "",
            ExplorerProcesses = explorerProcesses,
            TaskbarWindows = taskbarWindows,
            UiaRoots = uiaRoots,
            Findings = BuildFindings(primaryTaskbar, explorerProcesses, uiaRoots)
        };

        return report;
    }

    private static ExplorerProcessTarget CaptureExplorerProcess(
        Process process,
        IReadOnlyCollection<TaskbarWindowTarget> taskbarWindows)
    {
        var ownedTaskbarWindows = taskbarWindows
            .Where(window => window.ProcessId == process.Id)
            .OrderBy(window => window.Kind)
            .ToList();

        var modules = CaptureInterestingModules(process, out var moduleStatus);

        return new ExplorerProcessTarget
        {
            ProcessId = process.Id,
            ProcessName = process.ProcessName,
            MainModulePath = TryGetMainModulePath(process),
            MainWindowHandle = NativeMethods.FormatHandle(process.MainWindowHandle),
            StartTime = TryGetStartTime(process),
            OwnedTaskbarWindowHandles = ownedTaskbarWindows.Select(window => window.Handle).ToList(),
            OwnedTaskbarThreadIds = ownedTaskbarWindows
                .Select(window => window.ThreadId)
                .Where(threadId => threadId > 0)
                .Distinct()
                .Order()
                .ToList(),
            ModuleStatus = moduleStatus,
            InterestingModules = modules
        };
    }

    private static List<ProcessModuleTarget> CaptureInterestingModules(Process process, out string status)
    {
        var modules = new List<ProcessModuleTarget>();

        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                var moduleName = module.ModuleName ?? "";
                var filePath = module.FileName ?? "";
                var haystack = $"{moduleName} {filePath}";

                if (!InterestingModuleFragments.Any(fragment =>
                        haystack.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                modules.Add(new ProcessModuleTarget
                {
                    Name = moduleName,
                    FilePath = filePath,
                    BaseAddress = NativeMethods.FormatHandle(module.BaseAddress),
                    MemorySize = module.ModuleMemorySize
                });
            }

            status = $"Captured {modules.Count} interesting modules from {process.Modules.Count} loaded modules.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            status = $"Could not enumerate modules: {ex.GetType().Name}: {ex.Message}";
        }

        return modules
            .OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(module => module.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static UiaHookRootTarget CaptureUiaRootTarget(RootSnapshot root)
    {
        var entries = EnumeratePaths(root.UiaRoot!).ToList();
        var trayButtonEntries = entries
            .Where(entry => IsTrayButton(entry.Element))
            .Where(entry => !entry.Element.IsOffscreen && !entry.Element.BoundingRectangle.IsEmpty)
            .OrderBy(entry => entry.Element.BoundingRectangle.Left)
            .ThenBy(entry => entry.Element.BoundingRectangle.Top)
            .ToList();

        var commonPath = FindCommonPath(trayButtonEntries.Select(entry => entry.Path).ToList());
        var commonTarget = commonPath.Count == 0
            ? null
            : ToHookTarget(commonPath[^1], commonPath, "Deepest common UIA ancestor for visible tray buttons.");

        var movableTrayButtonEntries = trayButtonEntries
            .Where(entry => !IsFixedRightTrayButton(entry.Element))
            .ToList();
        var movableCommonPath = FindCommonPath(movableTrayButtonEntries.Select(entry => entry.Path).ToList());
        var movableCommonTarget = movableCommonPath.Count == 0
            ? null
            : ToHookTarget(movableCommonPath[^1], movableCommonPath, "Deepest common UIA ancestor for movable tray buttons.");

        return new UiaHookRootTarget
        {
            RootKind = root.Kind,
            RootHandle = root.Window.Handle,
            ProcessId = root.Window.ProcessId,
            ThreadId = root.Window.ThreadId,
            TrayButtonCount = trayButtonEntries.Count,
            MovableTrayButtonCount = movableTrayButtonEntries.Count,
            CommonTrayAncestor = commonTarget,
            MovableTrayAncestor = movableCommonTarget,
            TrayButtons = trayButtonEntries
                .Take(80)
                .Select(entry => ToHookTarget(entry.Element, entry.Path, "Visible Explorer-owned tray button."))
                .ToList(),
            FixedRightItems = trayButtonEntries
                .Where(entry => IsFixedRightTrayButton(entry.Element))
                .Select(entry => ToHookTarget(entry.Element, entry.Path, "Fixed right-side tray item to preserve."))
                .ToList(),
            Notes =
            [
                "This identifies the live Explorer/XAML target from outside the process.",
                "The next mutation-capable MVP would need an in-process hook against this target; UI Automation is only being used here as a locator."
            ]
        };
    }

    private static List<string> BuildFindings(
        TaskbarWindowTarget? primaryTaskbar,
        IReadOnlyCollection<ExplorerProcessTarget> explorerProcesses,
        IReadOnlyCollection<UiaHookRootTarget> uiaRoots)
    {
        var findings = new List<string>
        {
            "Hook target finder is read-only and did not inject into explorer.exe."
        };

        if (primaryTaskbar is null)
        {
            findings.Add("No primary Shell_TrayWnd target was found.");
        }
        else
        {
            findings.Add(
                $"Primary taskbar target is explorer.exe PID {primaryTaskbar.ProcessId}, " +
                $"thread {primaryTaskbar.ThreadId}, HWND {primaryTaskbar.Handle}.");
        }

        var commonTargets = uiaRoots
            .Where(root => root.CommonTrayAncestor is not null)
            .ToList();

        if (commonTargets.Count == 0)
        {
            findings.Add("No common UIA tray ancestor was found. Increase --max-uia-depth/--max-uia-elements before hook work.");
        }
        else
        {
            foreach (var target in commonTargets)
            {
                var ancestor = target.CommonTrayAncestor!;
                findings.Add(
                    $"{target.RootKind} common tray ancestor: class='{ancestor.ClassName}', " +
                    $"automationId='{ancestor.AutomationId}', depth={ancestor.Depth}, " +
                    $"children={ancestor.DirectChildCount}.");

                if (target.MovableTrayAncestor is not null)
                {
                    findings.Add(
                        $"{target.RootKind} movable tray target: buttons={target.MovableTrayButtonCount}, " +
                        $"class='{target.MovableTrayAncestor.ClassName}', " +
                        $"automationId='{target.MovableTrayAncestor.AutomationId}', " +
                        $"depth={target.MovableTrayAncestor.Depth}.");
                }
            }
        }

        var xamlModuleOwners = explorerProcesses
            .Where(process => process.InterestingModules.Any(module =>
                module.Name.Contains("xaml", StringComparison.OrdinalIgnoreCase) ||
                module.FilePath.Contains("xaml", StringComparison.OrdinalIgnoreCase)))
            .Select(process => process.ProcessId)
            .Distinct()
            .Order()
            .ToList();

        findings.Add(xamlModuleOwners.Count == 0
            ? "No loaded XAML-named module was visible in explorer.exe module enumeration."
            : $"Explorer processes with visible XAML modules: {string.Join(", ", xamlModuleOwners)}.");

        return findings;
    }

    private static IEnumerable<UiaPathEntry> EnumeratePaths(UiaElementSnapshot root)
    {
        var path = new List<UiaElementSnapshot>();
        foreach (var entry in EnumeratePaths(root, path))
        {
            yield return entry;
        }
    }

    private static IEnumerable<UiaPathEntry> EnumeratePaths(UiaElementSnapshot element, List<UiaElementSnapshot> path)
    {
        path.Add(element);
        yield return new UiaPathEntry(element, path.ToList());

        foreach (var child in element.Children)
        {
            foreach (var childEntry in EnumeratePaths(child, path))
            {
                yield return childEntry;
            }
        }

        path.RemoveAt(path.Count - 1);
    }

    private static List<UiaElementSnapshot> FindCommonPath(IReadOnlyCollection<List<UiaElementSnapshot>> paths)
    {
        if (paths.Count == 0)
        {
            return [];
        }

        var firstPath = paths.First();
        var shortestPathLength = paths.Min(path => path.Count);
        var common = new List<UiaElementSnapshot>();

        for (var index = 0; index < shortestPathLength; index++)
        {
            var candidate = firstPath[index];
            if (paths.All(path => ReferenceEquals(path[index], candidate)))
            {
                common.Add(candidate);
                continue;
            }

            break;
        }

        return common;
    }

    private static UiaHookElementTarget ToHookTarget(
        UiaElementSnapshot element,
        IReadOnlyList<UiaElementSnapshot> path,
        string reason)
    {
        return new UiaHookElementTarget
        {
            Reason = reason,
            Depth = Math.Max(0, path.Count - 1),
            Name = element.Name,
            AutomationId = element.AutomationId,
            ClassName = element.ClassName,
            FrameworkId = element.FrameworkId,
            ControlTypeName = element.ControlTypeName,
            ProcessId = element.ProcessId,
            IsOffscreen = element.IsOffscreen,
            DirectChildCount = element.Children.Count,
            BoundingRectangle = element.BoundingRectangle,
            Path = path
                .Select((segment, index) => new UiaPathSegment
                {
                    Depth = index,
                    Name = segment.Name,
                    AutomationId = segment.AutomationId,
                    ClassName = segment.ClassName,
                    ControlTypeName = segment.ControlTypeName,
                    BoundingRectangle = segment.BoundingRectangle
                })
                .ToList()
        };
    }

    private static bool IsTrayButton(UiaElementSnapshot element)
    {
        return string.Equals(element.ControlTypeName, "Button", StringComparison.OrdinalIgnoreCase) &&
               (string.Equals(element.AutomationId, "NotifyItemIcon", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(element.AutomationId, "SystemTrayIcon", StringComparison.OrdinalIgnoreCase) ||
                element.ClassName.StartsWith("SystemTray.", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFixedRightTrayButton(UiaElementSnapshot element)
    {
        return string.Equals(element.ClassName, "SystemTray.ShowDesktopButton", StringComparison.OrdinalIgnoreCase) ||
               (string.Equals(element.ClassName, "SystemTray.OmniButton", StringComparison.OrdinalIgnoreCase) &&
                element.Name.StartsWith("Clock ", StringComparison.OrdinalIgnoreCase));
    }

    private static string TryGetMainModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? "";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return $"<unavailable: {ex.GetType().Name}: {ex.Message}>";
        }
    }

    private static string TryGetStartTime(Process process)
    {
        try
        {
            return process.StartTime.ToString("O");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return $"<unavailable: {ex.GetType().Name}: {ex.Message}>";
        }
    }

    private sealed record UiaPathEntry(UiaElementSnapshot Element, List<UiaElementSnapshot> Path);
}

internal static class UiaReader
{
    private static readonly Guid CUIAutomationGuid = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");

    private const int UiaBoundingRectanglePropertyId = 30001;
    private const int UiaProcessIdPropertyId = 30002;
    private const int UiaControlTypePropertyId = 30003;
    private const int UiaNamePropertyId = 30005;
    private const int UiaAutomationIdPropertyId = 30011;
    private const int UiaClassNamePropertyId = 30012;
    private const int UiaIsOffscreenPropertyId = 30022;
    private const int UiaFrameworkIdPropertyId = 30024;

    private static readonly string[] InterestingTerms =
    [
        "tray",
        "notify",
        "notification",
        "overflow",
        "clock",
        "taskbar",
        "hidden",
        "system"
    ];

    public static UiaElementSnapshot? TryCapture(IntPtr handle, int maxDepth, int maxElements, out string status)
    {
        IUIAutomation? automation = null;
        try
        {
            var automationType = Type.GetTypeFromCLSID(CUIAutomationGuid, throwOnError: true)!;
            automation = (IUIAutomation)Activator.CreateInstance(automationType)!;

            var hr = automation.ElementFromHandle(handle, out var element);
            if (Failed(hr) || element is null)
            {
                status = $"IUIAutomation.ElementFromHandle failed. HRESULT 0x{hr:X8}.";
                return null;
            }

            hr = automation.get_RawViewWalker(out var walker);
            if (Failed(hr) || walker is null)
            {
                status = $"IUIAutomation.RawViewWalker failed. HRESULT 0x{hr:X8}.";
                return null;
            }

            if (element is null)
            {
                status = "IUIAutomation.ElementFromHandle returned null.";
                return null;
            }

            var remaining = maxElements;
            var snapshot = CaptureElement(walker, element, 0, maxDepth, ref remaining);
            status = remaining <= 0
                ? $"Captured UIA tree but stopped at {maxElements} elements."
                : "Captured UIA tree.";
            return snapshot;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            status = $"UI Automation capture failed: {ex.GetType().Name}: {ex.Message}";
            return null;
        }
        finally
        {
            if (automation is not null && Marshal.IsComObject(automation))
            {
                Marshal.ReleaseComObject(automation);
            }
        }
    }

    public static IEnumerable<UiaElementSnapshot> FindInterestingElements(UiaElementSnapshot root)
    {
        foreach (var element in Flatten(root))
        {
            var haystack = string.Join(
                ' ',
                element.Name,
                element.AutomationId,
                element.ClassName,
                element.FrameworkId,
                element.ControlTypeName);

            if (InterestingTerms.Any(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                yield return element;
            }
        }
    }

    public static TrayLayoutAnalysis? AnalyzeTrayLayout(IReadOnlyCollection<UiaElementSnapshot> elements)
    {
        var trayButtons = elements
            .Where(IsTrayButton)
            .Where(element => !element.IsOffscreen && !element.BoundingRectangle.IsEmpty)
            .OrderBy(element => element.BoundingRectangle.Left)
            .ToList();

        if (trayButtons.Count == 0)
        {
            return null;
        }

        var estimatedSlotWidth = EstimateSlot(trayButtons.Select(element => element.BoundingRectangle.Width));
        var estimatedSlotHeight = EstimateSlot(trayButtons.Select(element => element.BoundingRectangle.Height));
        var spanLeft = trayButtons.Min(element => element.BoundingRectangle.Left);
        var spanRight = trayButtons.Max(element => element.BoundingRectangle.Right);
        var buttonCount = trayButtons.Count;

        return new TrayLayoutAnalysis
        {
            TrayButtonCount = buttonCount,
            NotifyIconCount = trayButtons.Count(element => string.Equals(element.AutomationId, "NotifyItemIcon", StringComparison.OrdinalIgnoreCase)),
            SystemIconCount = trayButtons.Count(element => string.Equals(element.AutomationId, "SystemTrayIcon", StringComparison.OrdinalIgnoreCase)),
            RowCount = CountRows(trayButtons),
            EstimatedSlotWidth = estimatedSlotWidth,
            EstimatedSlotHeight = estimatedSlotHeight,
            SpanLeft = spanLeft,
            SpanRight = spanRight,
            SpanWidth = spanRight - spanLeft,
            TwoRowWidthAtCurrentSlot = (int)Math.Ceiling(buttonCount / 2.0) * estimatedSlotWidth,
            TwoRowWidthAt24PxSlot = (int)Math.Ceiling(buttonCount / 2.0) * 24,
            Capability = "UI Automation can map these Explorer-owned XAML tray buttons, but it does not provide a supported reposition/write API."
        };
    }

    public static List<TrayLayoutPlan> CreateLayoutPlans(
        IReadOnlyCollection<UiaElementSnapshot> elements,
        TrayLayoutAnalysis? analysis)
    {
        if (analysis is null)
        {
            return [];
        }

        var trayButtons = elements
            .Where(IsTrayButton)
            .Where(element => !element.IsOffscreen && !element.BoundingRectangle.IsEmpty)
            .OrderBy(element => element.BoundingRectangle.Left)
            .ToList();

        if (trayButtons.Count == 0)
        {
            return [];
        }

        return
        [
            BuildTwoRowPlan(
                "TwoRowNonClockTray",
                "Wrap non-clock, non-Show-Desktop tray buttons into two rows. Preserve current left-to-right order by assigning items column-major.",
                trayButtons,
                element => IsFixedRightTrayButton(element))
        ];
    }

    private static TrayLayoutPlan BuildTwoRowPlan(
        string name,
        string strategy,
        List<UiaElementSnapshot> allTrayButtons,
        Func<UiaElementSnapshot, bool> exclude)
    {
        const int requestedRows = 2;

        var candidates = allTrayButtons
            .Where(element => !exclude(element))
            .OrderBy(element => element.BoundingRectangle.Left)
            .ToList();
        var excluded = allTrayButtons
            .Where(exclude)
            .OrderBy(element => element.BoundingRectangle.Left)
            .ToList();

        if (candidates.Count == 0)
        {
            return new TrayLayoutPlan
            {
                Name = name,
                Strategy = strategy,
                RequestedRows = requestedRows,
                ExcludedButtonCount = excluded.Count,
                WriteCapability = "Read-only plan only. No supported UIA write API exists for Explorer-owned XAML tray layout."
            };
        }

        var measuredCurrentBounds = Bound(candidates.Select(GetEffectiveTrayRect));
        var allBounds = Bound(allTrayButtons.Select(GetEffectiveTrayRect));
        var fixedRightLeft = excluded.Count > 0
            ? excluded.Min(element => GetEffectiveTrayRect(element).Left)
            : allBounds.Right;
        var anchorRight = fixedRightLeft > measuredCurrentBounds.Left ? fixedRightLeft : measuredCurrentBounds.Right;
        var currentBounds = new RectSnapshot(measuredCurrentBounds.Left, measuredCurrentBounds.Top, anchorRight, measuredCurrentBounds.Bottom);
        var rowHeight = Math.Max(1, allBounds.Height / requestedRows);
        var columnCount = (int)Math.Ceiling(candidates.Count / (double)requestedRows);

        var assignments = candidates
            .Select((element, index) => new
            {
                Element = element,
                OriginalIndex = index,
                Column = index / requestedRows,
                Row = index % requestedRows
            })
            .ToList();

        var columnWidths = Enumerable.Range(0, columnCount)
            .Select(column =>
            {
                var width = assignments
                    .Where(assignment => assignment.Column == column)
                    .Select(assignment => GetEffectiveTrayRect(assignment.Element).Width)
                    .DefaultIfEmpty(0)
                    .Max();
                return Math.Max(1, width);
            })
            .ToArray();

        var proposedWidth = columnWidths.Sum();
        var proposedLeft = anchorRight - proposedWidth;
        var columnLefts = new int[columnCount];
        var currentLeft = proposedLeft;
        for (var column = 0; column < columnCount; column++)
        {
            columnLefts[column] = currentLeft;
            currentLeft += columnWidths[column];
        }

        var items = assignments
            .Select(assignment =>
            {
                var element = assignment.Element;
                var effectiveRect = GetEffectiveTrayRect(element);
                var proposedTop = allBounds.Top + assignment.Row * rowHeight;
                return new TrayLayoutPlanItem
                {
                    OriginalIndex = assignment.OriginalIndex,
                    Row = assignment.Row,
                    Column = assignment.Column,
                    Name = element.Name,
                    AutomationId = element.AutomationId,
                    ClassName = element.ClassName,
                    CurrentRect = effectiveRect,
                    ProposedRect = new RectSnapshot(
                        columnLefts[assignment.Column],
                        proposedTop,
                        columnLefts[assignment.Column] + effectiveRect.Width,
                        proposedTop + rowHeight)
                };
            })
            .ToList();

        var proposedBounds = Bound(items.Select(item => item.ProposedRect));

        return new TrayLayoutPlan
        {
            Name = name,
            Strategy = strategy,
            RequestedRows = requestedRows,
            ColumnCount = columnCount,
            PlannedButtonCount = candidates.Count,
            ExcludedButtonCount = excluded.Count,
            RowHeight = rowHeight,
            AnchorRight = anchorRight,
            CurrentCandidateBoundingRect = currentBounds,
            ProposedBoundingRect = proposedBounds,
            EstimatedWidthSaved = currentBounds.Width - proposedBounds.Width,
            WriteCapability = "Read-only plan only. No supported UIA write API exists for Explorer-owned XAML tray layout.",
            Items = items,
            ExcludedItems = excluded
                .Select(element => new TrayLayoutExcludedItem
                {
                    Reason = FixedRightReason(element),
                    Name = element.Name,
                    AutomationId = element.AutomationId,
                    ClassName = element.ClassName,
                    CurrentRect = GetEffectiveTrayRect(element)
                })
                .ToList(),
            Notes =
            [
                "This is geometry proof only; it does not move Explorer UI.",
                "The plan assumes the taskbar stays 48 px tall and divides the tray area into two 24 px rows on this screen.",
                "Clock and Show Desktop are kept fixed because they are not ordinary notification icons."
            ]
        };
    }

    private static UiaElementSnapshot CaptureElement(
        IUIAutomationTreeWalker walker,
        IUIAutomationElement element,
        int depth,
        int maxDepth,
        ref int remaining)
    {
        remaining--;

        var snapshot = new UiaElementSnapshot
        {
            Name = GetStringProperty(element, UiaNamePropertyId),
            AutomationId = GetStringProperty(element, UiaAutomationIdPropertyId),
            ClassName = GetStringProperty(element, UiaClassNamePropertyId),
            FrameworkId = GetStringProperty(element, UiaFrameworkIdPropertyId),
            ControlTypeName = GetControlTypeName(GetIntProperty(element, UiaControlTypePropertyId)),
            ProcessId = GetIntProperty(element, UiaProcessIdPropertyId),
            IsOffscreen = GetBoolProperty(element, UiaIsOffscreenPropertyId),
            BoundingRectangle = ToRectSnapshot(GetProperty(element, UiaBoundingRectanglePropertyId))
        };

        if (depth >= maxDepth || remaining <= 0)
        {
            return snapshot;
        }

        var child = GetFirstChild(walker, element);

        while (child is not null && remaining > 0)
        {
            snapshot.Children.Add(CaptureElement(walker, child, depth + 1, maxDepth, ref remaining));
            child = GetNextSibling(walker, child);
        }

        return snapshot;
    }

    private static IEnumerable<UiaElementSnapshot> Flatten(UiaElementSnapshot root)
    {
        yield return root;

        foreach (var child in root.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool IsTrayButton(UiaElementSnapshot element)
    {
        return string.Equals(element.ControlTypeName, "Button", StringComparison.OrdinalIgnoreCase) &&
               (string.Equals(element.AutomationId, "NotifyItemIcon", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(element.AutomationId, "SystemTrayIcon", StringComparison.OrdinalIgnoreCase) ||
                element.ClassName.StartsWith("SystemTray.", StringComparison.OrdinalIgnoreCase));
    }

    private static RectSnapshot GetEffectiveTrayRect(UiaElementSnapshot element)
    {
        var rect = element.BoundingRectangle;
        if (rect.IsEmpty)
        {
            return rect;
        }

        var width = rect.Width;
        var height = Math.Min(rect.Height, 48);

        if (string.Equals(element.AutomationId, "NotifyItemIcon", StringComparison.OrdinalIgnoreCase) && width > 48)
        {
            width = 24;
        }
        else if (element.ClassName.StartsWith("SystemTray.", StringComparison.OrdinalIgnoreCase) && width > 72)
        {
            width = 24;
        }
        else if (string.Equals(element.AutomationId, "NotifyItemIcon", StringComparison.OrdinalIgnoreCase) && width > 24)
        {
            width = 24;
        }
        else if (element.ClassName.StartsWith("SystemTray.", StringComparison.OrdinalIgnoreCase) &&
                 !IsFixedRightTrayButton(element) &&
                 width > 24)
        {
            width = 24;
        }

        return new RectSnapshot(rect.Left, rect.Top, rect.Left + width, rect.Top + height);
    }

    private static bool IsFixedRightTrayButton(UiaElementSnapshot element)
    {
        return string.Equals(element.ClassName, "SystemTray.ShowDesktopButton", StringComparison.OrdinalIgnoreCase) ||
               (string.Equals(element.ClassName, "SystemTray.OmniButton", StringComparison.OrdinalIgnoreCase) &&
                element.Name.StartsWith("Clock ", StringComparison.OrdinalIgnoreCase));
    }

    private static string FixedRightReason(UiaElementSnapshot element)
    {
        if (string.Equals(element.ClassName, "SystemTray.ShowDesktopButton", StringComparison.OrdinalIgnoreCase))
        {
            return "Show Desktop edge target";
        }

        if (string.Equals(element.ClassName, "SystemTray.OmniButton", StringComparison.OrdinalIgnoreCase) &&
            element.Name.StartsWith("Clock ", StringComparison.OrdinalIgnoreCase))
        {
            return "Clock/date block";
        }

        return "Fixed right-side tray item";
    }

    private static RectSnapshot Bound(IEnumerable<RectSnapshot> rects)
    {
        var nonEmpty = rects.Where(rect => !rect.IsEmpty).ToList();
        if (nonEmpty.Count == 0)
        {
            return RectSnapshot.Empty;
        }

        return new RectSnapshot(
            nonEmpty.Min(rect => rect.Left),
            nonEmpty.Min(rect => rect.Top),
            nonEmpty.Max(rect => rect.Right),
            nonEmpty.Max(rect => rect.Bottom));
    }

    private static int CountRows(List<UiaElementSnapshot> elements)
    {
        var rows = new List<int>();
        foreach (var top in elements.Select(element => element.BoundingRectangle.Top).Order())
        {
            if (rows.All(rowTop => Math.Abs(rowTop - top) > 2))
            {
                rows.Add(top);
            }
        }

        return rows.Count;
    }

    private static int EstimateSlot(IEnumerable<int> values)
    {
        var positive = values.Where(value => value > 0).Order().ToList();
        return positive.Count == 0 ? 0 : positive[positive.Count / 2];
    }

    private static IUIAutomationElement? GetFirstChild(IUIAutomationTreeWalker walker, IUIAutomationElement element)
    {
        var hr = walker.GetFirstChildElement(element, out var child);
        return Failed(hr) ? null : child;
    }

    private static IUIAutomationElement? GetNextSibling(IUIAutomationTreeWalker walker, IUIAutomationElement element)
    {
        var hr = walker.GetNextSiblingElement(element, out var sibling);
        return Failed(hr) ? null : sibling;
    }

    private static object? GetProperty(IUIAutomationElement element, int propertyId)
    {
        var hr = element.GetCurrentPropertyValue(propertyId, out var value);
        return Failed(hr) ? null : value;
    }

    private static string GetStringProperty(IUIAutomationElement element, int propertyId)
    {
        return Convert.ToString(GetProperty(element, propertyId)) ?? "";
    }

    private static int GetIntProperty(IUIAutomationElement element, int propertyId)
    {
        var value = GetProperty(element, propertyId);
        if (value is null)
        {
            return 0;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return 0;
        }
    }

    private static bool GetBoolProperty(IUIAutomationElement element, int propertyId)
    {
        var value = GetProperty(element, propertyId);
        if (value is null)
        {
            return false;
        }

        try
        {
            return Convert.ToBoolean(value);
        }
        catch
        {
            return false;
        }
    }

    private static RectSnapshot ToRectSnapshot(object? value)
    {
        if (value is null)
        {
            return RectSnapshot.Empty;
        }

        if (value is Array array && array.Length >= 4)
        {
            var left = Convert.ToDouble(array.GetValue(0));
            var top = Convert.ToDouble(array.GetValue(1));
            var width = Convert.ToDouble(array.GetValue(2));
            var height = Convert.ToDouble(array.GetValue(3));

            return new RectSnapshot(
                (int)Math.Round(left),
                (int)Math.Round(top),
                (int)Math.Round(left + width),
                (int)Math.Round(top + height));
        }

        var type = value.GetType();
        var leftValue = ReadDoubleProperty(type, value, "Left") ?? ReadDoubleProperty(type, value, "X");
        var topValue = ReadDoubleProperty(type, value, "Top") ?? ReadDoubleProperty(type, value, "Y");
        var widthValue = ReadDoubleProperty(type, value, "Width");
        var heightValue = ReadDoubleProperty(type, value, "Height");

        if (leftValue is null || topValue is null || widthValue is null || heightValue is null)
        {
            return RectSnapshot.Empty;
        }

        return new RectSnapshot(
            (int)Math.Round(leftValue.Value),
            (int)Math.Round(topValue.Value),
            (int)Math.Round(leftValue.Value + widthValue.Value),
            (int)Math.Round(topValue.Value + heightValue.Value));
    }

    private static double? ReadDoubleProperty(Type type, object instance, string name)
    {
        var property = type.GetProperty(name);
        if (property is null)
        {
            return null;
        }

        var value = property.GetValue(instance);
        return value is null ? null : Convert.ToDouble(value);
    }

    private static bool Failed(int hresult) => hresult < 0;

    private static string GetControlTypeName(int controlTypeId)
    {
        return controlTypeId switch
        {
            50000 => "Button",
            50001 => "Calendar",
            50002 => "CheckBox",
            50003 => "ComboBox",
            50004 => "Edit",
            50005 => "Hyperlink",
            50006 => "Image",
            50007 => "ListItem",
            50008 => "List",
            50009 => "Menu",
            50010 => "MenuBar",
            50011 => "MenuItem",
            50012 => "ProgressBar",
            50013 => "RadioButton",
            50014 => "ScrollBar",
            50015 => "Slider",
            50016 => "Spinner",
            50017 => "StatusBar",
            50018 => "Tab",
            50019 => "TabItem",
            50020 => "Text",
            50021 => "ToolBar",
            50022 => "ToolTip",
            50023 => "Tree",
            50024 => "TreeItem",
            50025 => "Custom",
            50026 => "Group",
            50027 => "Thumb",
            50028 => "DataGrid",
            50029 => "DataItem",
            50030 => "Document",
            50031 => "SplitButton",
            50032 => "Window",
            50033 => "Pane",
            50034 => "Header",
            50035 => "HeaderItem",
            50036 => "Table",
            50037 => "TitleBar",
            50038 => "Separator",
            50039 => "SemanticZoom",
            50040 => "AppBar",
            _ => controlTypeId == 0 ? "" : $"ControlType:{controlTypeId}"
        };
    }

    [ComImport]
    [Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        [PreserveSig]
        int CompareElements(IntPtr el1, IntPtr el2, out int areSame);

        [PreserveSig]
        int CompareRuntimeIds(IntPtr runtimeId1, IntPtr runtimeId2, out int areSame);

        [PreserveSig]
        int GetRootElement([MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement root);

        [PreserveSig]
        int ElementFromHandle(IntPtr hwnd, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);

        [PreserveSig]
        int ElementFromPoint(UiaPoint pt, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);

        [PreserveSig]
        int GetFocusedElement([MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);

        [PreserveSig]
        int GetRootElementBuildCache(IntPtr cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement root);

        [PreserveSig]
        int ElementFromHandleBuildCache(IntPtr hwnd, IntPtr cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);

        [PreserveSig]
        int ElementFromPointBuildCache(UiaPoint pt, IntPtr cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);

        [PreserveSig]
        int GetFocusedElementBuildCache(IntPtr cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);

        [PreserveSig]
        int CreateTreeWalker(IntPtr condition, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationTreeWalker walker);

        [PreserveSig]
        int get_ControlViewWalker([MarshalAs(UnmanagedType.Interface)] out IUIAutomationTreeWalker walker);

        [PreserveSig]
        int get_ContentViewWalker([MarshalAs(UnmanagedType.Interface)] out IUIAutomationTreeWalker walker);

        [PreserveSig]
        int get_RawViewWalker([MarshalAs(UnmanagedType.Interface)] out IUIAutomationTreeWalker walker);
    }

    [ComImport]
    [Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        [PreserveSig]
        int SetFocus();

        [PreserveSig]
        int GetRuntimeId(out IntPtr runtimeId);

        [PreserveSig]
        int FindFirst(int scope, IntPtr condition, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement found);

        [PreserveSig]
        int FindAll(int scope, IntPtr condition, out IntPtr found);

        [PreserveSig]
        int FindFirstBuildCache(int scope, IntPtr condition, IntPtr cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement found);

        [PreserveSig]
        int FindAllBuildCache(int scope, IntPtr condition, IntPtr cacheRequest, out IntPtr found);

        [PreserveSig]
        int BuildUpdatedCache(IntPtr cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement updatedElement);

        [PreserveSig]
        int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object value);
    }

    [ComImport]
    [Guid("4042C624-389C-4AFC-A630-9DF854A541FC")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTreeWalker
    {
        [PreserveSig]
        int NormalizeElement(
            [MarshalAs(UnmanagedType.Interface)] IUIAutomationElement element,
            [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement normalized);

        [PreserveSig]
        int GetParentElement(
            [MarshalAs(UnmanagedType.Interface)] IUIAutomationElement element,
            [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement parent);

        [PreserveSig]
        int GetFirstChildElement(
            [MarshalAs(UnmanagedType.Interface)] IUIAutomationElement element,
            [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement first);

        [PreserveSig]
        int GetLastChildElement(
            [MarshalAs(UnmanagedType.Interface)] IUIAutomationElement element,
            [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement last);

        [PreserveSig]
        int GetNextSiblingElement(
            [MarshalAs(UnmanagedType.Interface)] IUIAutomationElement element,
            [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement next);

        [PreserveSig]
        int GetPreviousSiblingElement(
            [MarshalAs(UnmanagedType.Interface)] IUIAutomationElement element,
            [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement previous);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UiaPoint
    {
        public double X;
        public double Y;
    }
}

internal static class ToolbarReader
{
    private const uint WmUser = 0x0400;
    private const uint TbGetButton = WmUser + 23;
    private const uint TbButtonCount = WmUser + 24;
    private const uint TbGetItemRect = WmUser + 29;
    private const uint TbGetButtonTextW = WmUser + 75;
    private const int RemoteBufferSize = 4096;
    private const int TButtonSize64 = 32;

    private const byte TbStateChecked = 0x01;
    private const byte TbStateEnabled = 0x04;
    private const byte TbStateHidden = 0x08;

    public static ToolbarReadResult TryRead(IntPtr toolbarHandle)
    {
        if (!NativeMethods.TrySendMessage(toolbarHandle, TbButtonCount, IntPtr.Zero, IntPtr.Zero, out var countResult))
        {
            return Failed("TB_BUTTONCOUNT timed out or failed.");
        }

        var buttonCount = countResult.ToInt32();
        if (buttonCount <= 0)
        {
            return new ToolbarReadResult
            {
                Summary = new ToolbarSummary
                {
                    CanReadButtons = true,
                    ReadStatus = "Toolbar reported zero buttons.",
                    ButtonCount = 0
                }
            };
        }

        if (buttonCount > 512)
        {
            return Failed($"Toolbar reported an unexpected button count: {buttonCount}.");
        }

        NativeMethods.GetWindowThreadProcessId(toolbarHandle, out var processId);
        var processHandle = NativeMethods.OpenProcess(
            NativeMethods.ProcessVmOperation | NativeMethods.ProcessVmRead | NativeMethods.ProcessVmWrite | NativeMethods.ProcessQueryLimitedInformation,
            false,
            processId);

        if (processHandle == IntPtr.Zero)
        {
            return Failed($"OpenProcess failed for PID {processId}. Win32 error {Marshal.GetLastWin32Error()}.");
        }

        try
        {
            var remoteBuffer = NativeMethods.VirtualAllocEx(
                processHandle,
                IntPtr.Zero,
                (UIntPtr)RemoteBufferSize,
                NativeMethods.MemCommit | NativeMethods.MemReserve,
                NativeMethods.PageReadWrite);

            if (remoteBuffer == IntPtr.Zero)
            {
                return Failed($"VirtualAllocEx failed. Win32 error {Marshal.GetLastWin32Error()}.");
            }

            try
            {
                var buttons = new List<ToolbarButtonSnapshot>();

                for (var index = 0; index < buttonCount; index++)
                {
                    if (!TryReadButton(toolbarHandle, processHandle, remoteBuffer, index, out var button))
                    {
                        continue;
                    }

                    buttons.Add(button);
                }

                return Succeeded(buttonCount, buttons);
            }
            finally
            {
                NativeMethods.VirtualFreeEx(processHandle, remoteBuffer, UIntPtr.Zero, NativeMethods.MemRelease);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(processHandle);
        }
    }

    private static bool TryReadButton(
        IntPtr toolbarHandle,
        IntPtr processHandle,
        IntPtr remoteBuffer,
        int index,
        out ToolbarButtonSnapshot button)
    {
        button = new ToolbarButtonSnapshot { Index = index };
        ZeroRemoteBuffer(processHandle, remoteBuffer, TButtonSize64);

        if (!NativeMethods.TrySendMessage(toolbarHandle, TbGetButton, new IntPtr(index), remoteBuffer, out var getButtonResult) ||
            getButtonResult == IntPtr.Zero)
        {
            return false;
        }

        var buttonBytes = new byte[TButtonSize64];
        if (!NativeMethods.ReadProcessMemory(processHandle, remoteBuffer, buttonBytes, buttonBytes.Length, out _))
        {
            return false;
        }

        var commandId = BinaryPrimitives.ReadInt32LittleEndian(buttonBytes.AsSpan(4, 4));
        var state = buttonBytes[8];
        var style = buttonBytes[9];
        var rect = ReadItemRect(toolbarHandle, processHandle, remoteBuffer, index);

        button = new ToolbarButtonSnapshot
        {
            Index = index,
            CommandId = commandId,
            Text = ReadButtonText(toolbarHandle, processHandle, remoteBuffer, commandId),
            StateHex = $"0x{state:X2}",
            StyleHex = $"0x{style:X2}",
            IsEnabled = (state & TbStateEnabled) != 0,
            IsHidden = (state & TbStateHidden) != 0,
            IsChecked = (state & TbStateChecked) != 0,
            RectClient = rect,
            RectScreen = NativeMethods.ClientRectToScreen(toolbarHandle, rect)
        };

        return true;
    }

    private static RectSnapshot ReadItemRect(IntPtr toolbarHandle, IntPtr processHandle, IntPtr remoteBuffer, int index)
    {
        ZeroRemoteBuffer(processHandle, remoteBuffer, NativeMethods.RectSize);

        if (!NativeMethods.TrySendMessage(toolbarHandle, TbGetItemRect, new IntPtr(index), remoteBuffer, out var result) ||
            result == IntPtr.Zero)
        {
            return RectSnapshot.Empty;
        }

        var rectBytes = new byte[NativeMethods.RectSize];
        if (!NativeMethods.ReadProcessMemory(processHandle, remoteBuffer, rectBytes, rectBytes.Length, out _))
        {
            return RectSnapshot.Empty;
        }

        return new RectSnapshot(
            BinaryPrimitives.ReadInt32LittleEndian(rectBytes.AsSpan(0, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(rectBytes.AsSpan(4, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(rectBytes.AsSpan(8, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(rectBytes.AsSpan(12, 4)));
    }

    private static string ReadButtonText(IntPtr toolbarHandle, IntPtr processHandle, IntPtr remoteBuffer, int commandId)
    {
        const int maxChars = 1024;
        var byteCount = maxChars * 2;
        ZeroRemoteBuffer(processHandle, remoteBuffer, byteCount);

        if (!NativeMethods.TrySendMessage(toolbarHandle, TbGetButtonTextW, new IntPtr(commandId), remoteBuffer, out var result) ||
            result.ToInt64() < 0)
        {
            return "";
        }

        var bytes = new byte[byteCount];
        if (!NativeMethods.ReadProcessMemory(processHandle, remoteBuffer, bytes, bytes.Length, out _))
        {
            return "";
        }

        var text = Encoding.Unicode.GetString(bytes);
        var terminator = text.IndexOf('\0');
        return terminator >= 0 ? text[..terminator] : text.TrimEnd('\0');
    }

    private static void ZeroRemoteBuffer(IntPtr processHandle, IntPtr remoteBuffer, int byteCount)
    {
        var zeros = new byte[byteCount];
        NativeMethods.WriteProcessMemory(processHandle, remoteBuffer, zeros, zeros.Length, out _);
    }

    private static ToolbarReadResult Succeeded(int reportedButtonCount, List<ToolbarButtonSnapshot> buttons)
    {
        var visible = buttons
            .Where(button => !button.IsHidden && !button.RectClient.IsEmpty)
            .ToList();

        return new ToolbarReadResult
        {
            Buttons = buttons,
            Summary = new ToolbarSummary
            {
                CanReadButtons = true,
                ReadStatus = buttons.Count == reportedButtonCount
                    ? "Read all reported buttons."
                    : $"Read {buttons.Count} of {reportedButtonCount} reported buttons.",
                ButtonCount = reportedButtonCount,
                VisibleButtonCount = visible.Count,
                HiddenButtonCount = buttons.Count(button => button.IsHidden),
                RowCount = CountRows(visible),
                EstimatedSlotWidth = EstimateSlot(visible.Select(button => button.RectClient.Width)),
                EstimatedSlotHeight = EstimateSlot(visible.Select(button => button.RectClient.Height))
            }
        };
    }

    private static ToolbarReadResult Failed(string status) => new()
    {
        Summary = new ToolbarSummary
        {
            CanReadButtons = false,
            ReadStatus = status
        }
    };

    private static int CountRows(List<ToolbarButtonSnapshot> visibleButtons)
    {
        var rows = new List<int>();
        foreach (var top in visibleButtons.Select(button => button.RectClient.Top).Order())
        {
            if (rows.All(rowTop => Math.Abs(rowTop - top) > 2))
            {
                rows.Add(top);
            }
        }

        return rows.Count;
    }

    private static int EstimateSlot(IEnumerable<int> values)
    {
        var positive = values.Where(value => value > 0).Order().ToList();
        return positive.Count == 0 ? 0 : positive[positive.Count / 2];
    }
}

internal static class NativeMethods
{
    public const int GwlStyle = -16;
    public const int GwlExStyle = -20;
    public const int RectSize = 16;

    public const uint ProcessVmOperation = 0x0008;
    public const uint ProcessVmRead = 0x0010;
    public const uint ProcessVmWrite = 0x0020;
    public const uint ProcessQueryLimitedInformation = 0x1000;

    public const uint MemCommit = 0x00001000;
    public const uint MemReserve = 0x00002000;
    public const uint MemRelease = 0x00008000;
    public const uint PageReadWrite = 0x04;

    private const uint SmtoAbortIfHung = 0x0002;
    private const uint MessageTimeoutMs = 1000;
    private const uint GwChild = 5;
    private const uint GwHwndNext = 2;

    public static IReadOnlyList<IntPtr> GetTopLevelWindows()
    {
        var windows = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            windows.Add(handle);
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public static IReadOnlyList<IntPtr> GetDirectChildWindows(IntPtr parent)
    {
        var children = new List<IntPtr>();

        var child = GetWindow(parent, GwChild);
        while (child != IntPtr.Zero)
        {
            children.Add(child);
            child = GetWindow(child, GwHwndNext);
        }

        return children;
    }

    public static bool TrySendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        var sent = SendMessageTimeout(
            handle,
            message,
            wParam,
            lParam,
            SmtoAbortIfHung,
            MessageTimeoutMs,
            out result);

        return sent != IntPtr.Zero;
    }

    public static string GetClassNameText(IntPtr handle)
    {
        var builder = new StringBuilder(512);
        var length = GetClassName(handle, builder, builder.Capacity);
        return length <= 0 ? "" : builder.ToString();
    }

    public static string GetWindowTextSafe(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        var capacity = Math.Clamp(length + 1, 256, 4096);
        var builder = new StringBuilder(capacity);
        var copied = GetWindowText(handle, builder, builder.Capacity);
        return copied <= 0 ? "" : builder.ToString();
    }

    public static RectSnapshot GetWindowRectSnapshot(IntPtr handle)
    {
        return GetWindowRect(handle, out var rect)
            ? new RectSnapshot(rect.Left, rect.Top, rect.Right, rect.Bottom)
            : RectSnapshot.Empty;
    }

    public static RectSnapshot GetClientRectOnScreenSnapshot(IntPtr handle)
    {
        if (!GetClientRect(handle, out var rect))
        {
            return RectSnapshot.Empty;
        }

        return ClientRectToScreen(handle, new RectSnapshot(rect.Left, rect.Top, rect.Right, rect.Bottom));
    }

    public static RectSnapshot ClientRectToScreen(IntPtr handle, RectSnapshot rect)
    {
        if (rect.IsEmpty)
        {
            return RectSnapshot.Empty;
        }

        var topLeft = new POINT { X = rect.Left, Y = rect.Top };
        var bottomRight = new POINT { X = rect.Right, Y = rect.Bottom };

        return ClientToScreen(handle, ref topLeft) && ClientToScreen(handle, ref bottomRight)
            ? new RectSnapshot(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y)
            : RectSnapshot.Empty;
    }

    public static string GetWindowLongPtrHex(IntPtr handle, int index)
    {
        var value = GetWindowLongPtr(handle, index);
        return $"0x{value.ToInt64():X}";
    }

    public static string FormatHandle(IntPtr handle) => $"0x{handle.ToInt64():X}";

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out UIntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out UIntPtr lpNumberOfBytesWritten);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
