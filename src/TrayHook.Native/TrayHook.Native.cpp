#define WIN32_LEAN_AND_MEAN
#define NOMINMAX

#include <windows.h>
#include <ocidl.h>
#include <xamlom.h>

#ifdef GetCurrentTime
#undef GetCurrentTime
#endif

#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Hosting.h>
#include <winrt/Windows.UI.Xaml.Media.h>

#include <algorithm>
#include <atomic>
#include <cstdio>
#include <functional>
#include <string>
#include <vector>

namespace wf = winrt::Windows::Foundation;
namespace wux = winrt::Windows::UI::Xaml;
namespace wuxc = winrt::Windows::UI::Xaml::Controls;
namespace wuxh = winrt::Windows::UI::Xaml::Hosting;
namespace wuxm = winrt::Windows::UI::Xaml::Media;

// Must match TrayHookController.NativeMethods.TrayWrapDoublerTapClsid.
const CLSID CLSID_TrayWrapDoublerTap = {
    0x6f1dc928,
    0x7c3d,
    0x4a9e,
    {0xa2, 0x58, 0xe5, 0x53, 0x8f, 0xa8, 0x3f, 0xa2}};

struct HookSettings {
    int rows = 2;
    int width = 24;
    bool reset = false;
    bool dumpTree = false;
    std::wstring statePath;
};

std::atomic_bool g_promoteAllIcons = false;
std::atomic_bool g_promoterStarted = false;
std::atomic_bool g_treeDumped = false;
std::wstring g_promotionStatePath;

void Log(PCWSTR format, ...) {
    WCHAR message[1024]{};
    va_list args;
    va_start(args, format);
    _vsnwprintf_s(message, _TRUNCATE, format, args);
    va_end(args);

    OutputDebugStringW(L"[SystrayWrapDoubler] ");
    OutputDebugStringW(message);
    OutputDebugStringW(L"\n");

    WCHAR tempPath[MAX_PATH]{};
    if (!GetTempPathW(ARRAYSIZE(tempPath), tempPath)) {
        return;
    }

    std::wstring logPath = tempPath;
    logPath += L"SystrayWrapDoubler.Native.log";

    FILE* file = nullptr;
    if (_wfopen_s(&file, logPath.c_str(), L"a, ccs=UTF-8") == 0 && file) {
        fwprintf(file, L"%s\n", message);
        fclose(file);
    }
}

int ReadIntSetting(const std::wstring& data, PCWSTR key, int fallback, int minValue, int maxValue) {
    const std::wstring prefix = std::wstring(key) + L"=";
    const auto start = data.find(prefix);
    if (start == std::wstring::npos) {
        return fallback;
    }

    const auto valueStart = start + prefix.size();
    const auto valueEnd = data.find(L';', valueStart);
    const auto text = data.substr(valueStart, valueEnd == std::wstring::npos ? valueEnd : valueEnd - valueStart);
    const int value = _wtoi(text.c_str());
    return std::clamp(value, minValue, maxValue);
}

bool HasSetting(const std::wstring& data, PCWSTR text) {
    return data.find(text) != std::wstring::npos;
}

std::wstring ReadStringSetting(const std::wstring& data, PCWSTR key) {
    const std::wstring prefix = std::wstring(key) + L"=";
    const auto start = data.find(prefix);
    if (start == std::wstring::npos) {
        return L"";
    }

    const auto valueStart = start + prefix.size();
    const auto valueEnd = data.find(L';', valueStart);
    return data.substr(valueStart, valueEnd == std::wstring::npos ? valueEnd : valueEnd - valueStart);
}

HookSettings LoadSettings(IXamlDiagnostics* diagnostics) {
    HookSettings settings;
    if (!diagnostics) {
        return settings;
    }

    BSTR initializationData = nullptr;
    if (SUCCEEDED(diagnostics->GetInitializationData(&initializationData)) && initializationData) {
        std::wstring data(initializationData, SysStringLen(initializationData));
        settings.reset = HasSetting(data, L"mode=reset");
        settings.dumpTree = HasSetting(data, L"dump=1");
        settings.rows = ReadIntSetting(data, L"rows", settings.rows, 1, 4);
        settings.width = ReadIntSetting(data, L"width", settings.width, 16, 48);
        settings.statePath = ReadStringSetting(data, L"state");
        SysFreeString(initializationData);
    }

    return settings;
}

template <typename T>
winrt::com_ptr<T> QueryInterfaceFrom(IUnknown* unknown) {
    winrt::com_ptr<T> result;
    if (unknown) {
        unknown->QueryInterface(__uuidof(T), result.put_void());
    }

    return result;
}

bool EnumChildElements(
    const wux::FrameworkElement& element,
    const std::function<bool(wux::FrameworkElement)>& callback) {
    if (!element) {
        return false;
    }

    const int childCount = wuxm::VisualTreeHelper::GetChildrenCount(element);
    for (int index = 0; index < childCount; index++) {
        auto child = wuxm::VisualTreeHelper::GetChild(element, index).try_as<wux::FrameworkElement>();
        if (!child) {
            continue;
        }

        if (callback(child)) {
            return true;
        }

        if (EnumChildElements(child, callback)) {
            return true;
        }
    }

    return false;
}

wux::FrameworkElement FindChildByName(const wux::FrameworkElement& element, PCWSTR name) {
    wux::FrameworkElement result{nullptr};
    EnumChildElements(element, [&](wux::FrameworkElement child) {
        if (child.Name() == name) {
            result = child;
            return true;
        }

        return false;
    });

    return result;
}

wux::FrameworkElement FindChildByClassName(const wux::FrameworkElement& element, PCWSTR className) {
    wux::FrameworkElement result{nullptr};
    EnumChildElements(element, [&](wux::FrameworkElement child) {
        if (winrt::get_class_name(child) == className) {
            result = child;
            return true;
        }

        return false;
    });

    return result;
}

wux::FrameworkElement FindAncestorByName(wux::FrameworkElement element, PCWSTR name) {
    while (element) {
        if (element.Name() == name) {
            return element;
        }

        element = wuxm::VisualTreeHelper::GetParent(element).try_as<wux::FrameworkElement>();
    }

    return nullptr;
}

bool PromotionStateAlreadyRecorded(PCWSTR entry) {
    if (g_promotionStatePath.empty()) {
        return true;
    }

    FILE* file = nullptr;
    if (_wfopen_s(&file, g_promotionStatePath.c_str(), L"r, ccs=UTF-8") != 0 || !file) {
        return false;
    }

    WCHAR line[512]{};
    const std::wstring prefix = std::wstring(entry) + L"\t";
    bool found = false;
    while (fgetws(line, ARRAYSIZE(line), file)) {
        if (wcsncmp(line, prefix.c_str(), prefix.size()) == 0) {
            found = true;
            break;
        }
    }

    fclose(file);
    return found;
}

void RecordPromotionState(PCWSTR entry, LONG readResult, DWORD type, DWORD value) {
    if (g_promotionStatePath.empty() || PromotionStateAlreadyRecorded(entry)) {
        return;
    }

    FILE* file = nullptr;
    if (_wfopen_s(&file, g_promotionStatePath.c_str(), L"a, ccs=UTF-8") != 0 || !file) {
        Log(L"Could not open promotion state file: %s", g_promotionStatePath.c_str());
        return;
    }

    if (readResult == ERROR_SUCCESS && type == REG_DWORD) {
        fwprintf(file, L"%s\tdword\t%lu\n", entry, value);
    } else {
        fwprintf(file, L"%s\tmissing\t0\n", entry);
    }

    fclose(file);
}

void PromoteNotifyIconSettingsOnce() {
    constexpr WCHAR baseKeyPath[] = L"Control Panel\\NotifyIconSettings";
    constexpr WCHAR tempValueName[] = L"_temp_systray_wrap_doubler_touch";

    HKEY baseKey = nullptr;
    LONG result = RegOpenKeyExW(HKEY_CURRENT_USER, baseKeyPath, 0, KEY_READ | KEY_WRITE, &baseKey);
    if (result != ERROR_SUCCESS) {
        Log(L"PromoteNotifyIconSettingsOnce could not open base key: %ld", result);
        return;
    }

    DWORD changedCount = 0;
    DWORD checkedCount = 0;
    DWORD index = 0;
    WCHAR subKeyName[256]{};
    DWORD subKeyNameSize = ARRAYSIZE(subKeyName);

    while (RegEnumKeyExW(baseKey, index, subKeyName, &subKeyNameSize, nullptr, nullptr, nullptr, nullptr) == ERROR_SUCCESS) {
        HKEY subKey = nullptr;
        result = RegOpenKeyExW(baseKey, subKeyName, 0, KEY_READ | KEY_SET_VALUE, &subKey);
        if (result == ERROR_SUCCESS) {
            checkedCount++;

            DWORD promoted = 0;
            DWORD promotedSize = sizeof(promoted);
            DWORD type = 0;
            const LONG readResult = RegQueryValueExW(
                subKey,
                L"IsPromoted",
                nullptr,
                &type,
                reinterpret_cast<BYTE*>(&promoted),
                &promotedSize);

            if (readResult != ERROR_SUCCESS || type != REG_DWORD || promoted != 1) {
                RecordPromotionState(subKeyName, readResult, type, promoted);
                promoted = 1;
                result = RegSetValueExW(
                    subKey,
                    L"IsPromoted",
                    0,
                    REG_DWORD,
                    reinterpret_cast<const BYTE*>(&promoted),
                    sizeof(promoted));

                if (result == ERROR_SUCCESS) {
                    RegSetValueExW(subKey, tempValueName, 0, REG_SZ, reinterpret_cast<const BYTE*>(L""), sizeof(WCHAR));
                    RegDeleteValueW(subKey, tempValueName);
                    changedCount++;
                } else {
                    Log(L"Failed to set IsPromoted for %s: %ld", subKeyName, result);
                }
            }

            RegCloseKey(subKey);
        }

        index++;
        subKeyNameSize = ARRAYSIZE(subKeyName);
    }

    RegCloseKey(baseKey);

    if (changedCount > 0) {
        Log(L"Promoted notify icon settings: changed=%lu checked=%lu", changedCount, checkedCount);
    }
}

DWORD WINAPI PromotionWorkerProc(LPVOID) {
    while (true) {
        if (g_promoteAllIcons.load()) {
            PromoteNotifyIconSettingsOnce();
        }

        Sleep(2000);
    }
}

void SetPromotionWorkerEnabled(bool enabled, const std::wstring& statePath) {
    g_promoteAllIcons = enabled;
    if (enabled) {
        g_promotionStatePath = statePath;
        PromoteNotifyIconSettingsOnce();

        if (!g_promoterStarted.exchange(true)) {
            HANDLE thread = CreateThread(nullptr, 0, PromotionWorkerProc, nullptr, 0, nullptr);
            if (thread) {
                CloseHandle(thread);
                Log(L"Promotion worker started");
            } else {
                Log(L"Promotion worker failed to start: %lu", GetLastError());
            }
        }
    } else {
        g_promotionStatePath.clear();
        Log(L"Promotion worker disabled");
    }
}

void DumpElementTree(const wux::FrameworkElement& element, int depth, int maxDepth) {
    if (!element || depth > maxDepth) {
        return;
    }

    const std::wstring indent(depth * 2, L' ');
    const auto name = element.Name();
    const auto className = winrt::get_class_name(element);

    Log(L"%s%s name='%s' actual=%.1fx%.1f size=%.1fx%.1f",
        indent.c_str(),
        className.c_str(),
        name.c_str(),
        element.ActualWidth(),
        element.ActualHeight(),
        element.Width(),
        element.Height());

    const int childCount = wuxm::VisualTreeHelper::GetChildrenCount(element);
    for (int index = 0; index < childCount; index++) {
        auto child = wuxm::VisualTreeHelper::GetChild(element, index).try_as<wux::FrameworkElement>();
        DumpElementTree(child, depth + 1, maxDepth);
    }
}

void StyleIconSlotElement(const wux::FrameworkElement& element, int size, bool forceWidth = true) {
    if (!element) {
        return;
    }

    element.ClearValue(wux::UIElement::ClipProperty());
    element.MinHeight(size);
    element.Height(size);
    element.MaxHeight(size);
    element.VerticalAlignment(wux::VerticalAlignment::Center);

    if (forceWidth) {
        element.MinWidth(size);
        element.Width(size);
        element.MaxWidth(size);
        element.HorizontalAlignment(wux::HorizontalAlignment::Center);
    }
}

bool IsIconSlotDescendant(const wux::FrameworkElement& element) {
    const auto name = element.Name();
    const auto className = winrt::get_class_name(element);

    return name == L"ContainerGrid" ||
           name == L"ContentPresenter" ||
           name == L"ContentGrid" ||
           name == L"BackgroundBorder" ||
           className == L"SystemTray.TextIconContent" ||
           className == L"SystemTray.ImageIconContent" ||
           className == L"SystemTray.LanguageTextIconContent";
}

void StyleNotifyIconView(const wux::FrameworkElement& notifyIconView, int width) {
    if (!notifyIconView) {
        return;
    }

    StyleIconSlotElement(notifyIconView, width);

    EnumChildElements(notifyIconView, [width](wux::FrameworkElement descendant) {
        if (IsIconSlotDescendant(descendant)) {
            StyleIconSlotElement(descendant, width);
        }

        return false;
    });

    auto child = notifyIconView;
    if ((child = FindChildByName(child, L"ContainerGrid")) &&
        (child = FindChildByName(child, L"ContentPresenter")) &&
        (child = FindChildByName(child, L"ContentGrid"))) {
        EnumChildElements(child, [width](wux::FrameworkElement contentChild) {
            const auto className = winrt::get_class_name(contentChild);
            if (className == L"SystemTray.TextIconContent" ||
                className == L"SystemTray.ImageIconContent") {
                auto containerGrid = FindChildByName(contentChild, L"ContainerGrid").try_as<wuxc::Grid>();
                if (containerGrid) {
                    containerGrid.Padding(wux::Thickness{});
                }
            } else if (className == L"SystemTray.LanguageTextIconContent") {
                contentChild.ClearValue(wux::FrameworkElement::WidthProperty());
                contentChild.MinWidth(width + 12);
            }

            return false;
        });
    }
}

void StyleSystemTrayIcon(const wux::FrameworkElement& systemTrayIcon, int width) {
    if (!systemTrayIcon) {
        return;
    }

    StyleIconSlotElement(systemTrayIcon, width);

    EnumChildElements(systemTrayIcon, [width](wux::FrameworkElement descendant) {
        if (IsIconSlotDescendant(descendant)) {
            StyleIconSlotElement(descendant, width);
        }

        return false;
    });

    auto child = systemTrayIcon;
    if ((child = FindChildByName(child, L"ContainerGrid")) &&
        (child = FindChildByName(child, L"ContentGrid")) &&
        (child = FindChildByClassName(child, L"SystemTray.TextIconContent")) &&
        (child = FindChildByName(child, L"ContainerGrid"))) {
        auto containerGrid = child.try_as<wuxc::Grid>();
        if (containerGrid) {
            containerGrid.Padding(wux::Thickness{4, 0, 4, 0});
        }
    }
}

void ClearElementSize(const wux::FrameworkElement& element) {
    if (!element) {
        return;
    }

    element.ClearValue(wux::FrameworkElement::WidthProperty());
    element.ClearValue(wux::FrameworkElement::MinWidthProperty());
    element.ClearValue(wux::FrameworkElement::MaxWidthProperty());
    element.ClearValue(wux::FrameworkElement::HeightProperty());
    element.ClearValue(wux::FrameworkElement::MinHeightProperty());
    element.ClearValue(wux::FrameworkElement::MaxHeightProperty());
    element.ClearValue(wux::UIElement::ClipProperty());
}

void PrepareTwoRowContainer(const wux::FrameworkElement& element, double width, double height) {
    if (!element) {
        return;
    }

    element.ClearValue(wux::UIElement::ClipProperty());
    element.MinHeight(height);
    element.Height(height);
    element.VerticalAlignment(wux::VerticalAlignment::Stretch);

    if (width > 0) {
        element.MinWidth(width);
        element.Width(width);
        element.MaxWidth(width);
    }
}

double GetGridHeightFromAncestors(const wux::FrameworkElement& element, int rows, int width) {
    double height = static_cast<double>(std::max(32, rows * width));
    auto ancestor = element;
    while (ancestor) {
        height = std::max(height, ancestor.ActualHeight());
        if (ancestor.Name() == L"SystemTrayFrameGrid") {
            break;
        }

        ancestor = wuxm::VisualTreeHelper::GetParent(ancestor).try_as<wux::FrameworkElement>();
    }

    return height;
}

void ResetStackPanelGrid(const wux::FrameworkElement& stackPanel) {
    if (!stackPanel) {
        return;
    }

    const int childCount = wuxm::VisualTreeHelper::GetChildrenCount(stackPanel);
    int resetCount = 0;
    for (int index = 0; index < childCount; index++) {
        auto child = wuxm::VisualTreeHelper::GetChild(stackPanel, index).try_as<wux::FrameworkElement>();
        if (!child || winrt::get_class_name(child) != L"Windows.UI.Xaml.Controls.ContentPresenter") {
            continue;
        }

        ClearElementSize(child);
        child.ClearValue(wux::UIElement::RenderTransformProperty());

        auto notifyIconView = FindChildByName(child, L"NotifyItemIcon");
        ClearElementSize(notifyIconView);

        auto systemTrayIcon = FindChildByName(child, L"SystemTrayIcon");
        ClearElementSize(systemTrayIcon);

        auto chevronIconView = FindChildByClassName(child, L"SystemTray.ChevronIconView");
        ClearElementSize(chevronIconView);

        resetCount++;
    }

    stackPanel.ClearValue(wux::FrameworkElement::WidthProperty());
    UNREFERENCED_PARAMETER(resetCount);
}

void ApplyStackPanelGrid(const wux::FrameworkElement& stackPanel, const HookSettings& settings) {
    if (!stackPanel) {
        return;
    }

    if (settings.reset) {
        ResetStackPanelGrid(stackPanel);
        return;
    }

    const int rows = std::max(1, settings.rows);
    const int width = std::max(16, settings.width);
    const int childCount = wuxm::VisualTreeHelper::GetChildrenCount(stackPanel);
    const double stackPanelHeight = GetGridHeightFromAncestors(stackPanel, rows, width);
    const double itemHeight = rows > 1 ? stackPanelHeight / rows : stackPanelHeight;

    std::vector<wux::FrameworkElement> presenters;
    for (int index = 0; index < childCount; index++) {
        auto child = wuxm::VisualTreeHelper::GetChild(stackPanel, index).try_as<wux::FrameworkElement>();
        if (!child || winrt::get_class_name(child) != L"Windows.UI.Xaml.Controls.ContentPresenter") {
            continue;
        }

        presenters.push_back(child);
    }

    const int presenterCount = static_cast<int>(presenters.size());
    const int cols = (presenterCount + rows - 1) / rows;
    const double desiredWidth = width * std::max(1, cols);

    if (rows > 1) {
        PrepareTwoRowContainer(stackPanel, desiredWidth, stackPanelHeight);
    } else {
        stackPanel.Width(desiredWidth);
    }

    for (auto const& child : presenters) {
        child.ClearValue(wux::UIElement::RenderTransformProperty());
        child.ClearValue(wux::UIElement::ClipProperty());
        child.Width(width);
        child.MinWidth(width);
        child.MaxWidth(width);
        child.Height(itemHeight);
        child.MinHeight(itemHeight);
        child.MaxHeight(itemHeight);
        child.VerticalAlignment(wux::VerticalAlignment::Top);

        auto notifyIconView = FindChildByName(child, L"NotifyItemIcon");
        if (notifyIconView) {
            StyleNotifyIconView(notifyIconView, width);
        }

        auto systemTrayIcon = FindChildByName(child, L"SystemTrayIcon");
        if (systemTrayIcon) {
            StyleSystemTrayIcon(systemTrayIcon, width);
        }

        auto chevronIconView = FindChildByClassName(child, L"SystemTray.ChevronIconView");
        if (chevronIconView) {
            StyleNotifyIconView(chevronIconView, width);
        }
    }

    stackPanel.UpdateLayout();

    for (int visualIndex = 0; visualIndex < presenterCount; visualIndex++) {
        const auto& child = presenters[visualIndex];
        const int col = visualIndex / rows;
        const int row = visualIndex % rows;
        const double targetX = width * col;
        const double targetY = itemHeight * row;

        wf::Point localPosition{};
        try {
            localPosition = child.TransformToVisual(stackPanel).TransformPoint(wf::Point{0, 0});
        } catch (...) {
            localPosition = wf::Point{static_cast<float>(width * visualIndex), 0};
        }

        wuxm::TranslateTransform transform;
        transform.X(targetX - localPosition.X);
        transform.Y(targetY - localPosition.Y);
        child.RenderTransform(transform);
    }

    UNREFERENCED_PARAMETER(childCount);
    UNREFERENCED_PARAMETER(cols);
}

bool ApplyNotificationAreaIcons(const wux::FrameworkElement& notificationAreaIcons, const HookSettings& settings) {
    if (!notificationAreaIcons) {
        return false;
    }

    auto itemsPresenter = FindChildByClassName(notificationAreaIcons, L"Windows.UI.Xaml.Controls.ItemsPresenter");
    if (!itemsPresenter) {
        return false;
    }

    auto stackPanel = FindChildByClassName(itemsPresenter, L"Windows.UI.Xaml.Controls.StackPanel");
    if (!stackPanel) {
        return false;
    }

    ApplyStackPanelGrid(stackPanel, settings);
    return true;
}

bool ApplyControlCenterButtonStyle(const wux::FrameworkElement& controlCenterButton, const HookSettings& settings) {
    if (!controlCenterButton) {
        return false;
    }

    auto stackPanel = FindChildByClassName(controlCenterButton, L"Windows.UI.Xaml.Controls.StackPanel");
    if (!stackPanel) {
        return false;
    }

    const int rows = std::max(1, settings.rows);
    const int width = std::max(16, settings.width);
    const double height = GetGridHeightFromAncestors(controlCenterButton, rows, width);
    PrepareTwoRowContainer(controlCenterButton, 0, height);
    ApplyStackPanelGrid(stackPanel, settings);
    return true;
}

bool ApplyIconStackStyle(PCWSTR containerName, const wux::FrameworkElement& container, const HookSettings& settings) {
    if (!container) {
        return false;
    }

    auto content = FindChildByName(container, L"Content");
    auto iconStack = content ? FindChildByName(content, L"IconStack") : nullptr;
    auto itemsPresenter = iconStack ? FindChildByClassName(iconStack, L"Windows.UI.Xaml.Controls.ItemsPresenter") : nullptr;
    auto stackPanel = itemsPresenter ? FindChildByClassName(itemsPresenter, L"Windows.UI.Xaml.Controls.StackPanel") : nullptr;
    if (!stackPanel) {
        return false;
    }

    const int rows = std::max(1, settings.rows);
    const int width = std::max(16, settings.width);
    const double height = GetGridHeightFromAncestors(container, rows, width);
    PrepareTwoRowContainer(container, 0, height);
    PrepareTwoRowContainer(content, 0, height);
    PrepareTwoRowContainer(iconStack, 0, height);
    PrepareTwoRowContainer(itemsPresenter, 0, height);
    ApplyStackPanelGrid(stackPanel, settings);
    UNREFERENCED_PARAMETER(containerName);
    return true;
}

bool ApplySystemTrayFrameGrid(const wux::FrameworkElement& systemTrayFrameGrid, const HookSettings& settings) {
    if (!systemTrayFrameGrid) {
        return false;
    }

    if (settings.dumpTree && !g_treeDumped.exchange(true)) {
        Log(L"SystemTrayFrameGrid tree dump begin");
        DumpElementTree(systemTrayFrameGrid, 0, 9);
        Log(L"SystemTrayFrameGrid tree dump end");
    }

    bool applied = false;

    auto notificationAreaIcons = FindChildByName(systemTrayFrameGrid, L"NotificationAreaIcons");
    if (notificationAreaIcons) {
        applied |= ApplyNotificationAreaIcons(notificationAreaIcons, settings);
    }

    auto controlCenterButton = FindChildByName(systemTrayFrameGrid, L"ControlCenterButton");
    if (controlCenterButton) {
        applied |= ApplyControlCenterButtonStyle(controlCenterButton, settings);
    }

    for (PCWSTR containerName : {L"NotifyIconStack", L"MainStack", L"NonActivatableStack"}) {
        auto container = FindChildByName(systemTrayFrameGrid, containerName);
        if (container) {
            applied |= ApplyIconStackStyle(containerName, container, settings);
        }
    }

    return applied;
}

bool TryApplyFromElement(const wux::FrameworkElement& element, const HookSettings& settings) {
    if (!element) {
        return false;
    }

    if (element.Name() == L"SystemTrayFrameGrid") {
        return ApplySystemTrayFrameGrid(element, settings);
    }

    auto ancestorFrameGrid = FindAncestorByName(element, L"SystemTrayFrameGrid");
    if (ancestorFrameGrid) {
        return ApplySystemTrayFrameGrid(ancestorFrameGrid, settings);
    }

    auto descendantFrameGrid = FindChildByName(element, L"SystemTrayFrameGrid");
    if (descendantFrameGrid) {
        return ApplySystemTrayFrameGrid(descendantFrameGrid, settings);
    }

    if (element.Name() == L"NotificationAreaIcons") {
        return ApplyNotificationAreaIcons(element, settings);
    }

    auto ancestorNotificationAreaIcons = FindAncestorByName(element, L"NotificationAreaIcons");
    if (ancestorNotificationAreaIcons) {
        return ApplyNotificationAreaIcons(ancestorNotificationAreaIcons, settings);
    }

    auto notificationAreaIcons = FindChildByName(element, L"NotificationAreaIcons");
    if (notificationAreaIcons) {
        return ApplyNotificationAreaIcons(notificationAreaIcons, settings);
    }

    return false;
}

struct VisualTreeWatcher : winrt::implements<VisualTreeWatcher, IVisualTreeServiceCallback2, winrt::non_agile> {
    void Configure(winrt::com_ptr<IXamlDiagnostics> diagnostics, HookSettings settings) {
        m_diagnostics = std::move(diagnostics);
        m_settings = settings;
    }

    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(
        ParentChildRelation,
        VisualElement element,
        VisualMutationType) noexcept override {
        try {
            if (!m_diagnostics) {
                return S_OK;
            }

            auto frameworkElement = FromHandle<wux::FrameworkElement>(element.Handle);
            if (!frameworkElement) {
                auto desktopXamlSource = FromHandle<wuxh::DesktopWindowXamlSource>(element.Handle);
                if (desktopXamlSource) {
                    frameworkElement = desktopXamlSource.Content().try_as<wux::FrameworkElement>();
                }
            }

            if (!ShouldApplyNow()) {
                return S_OK;
            }

            ScopedApplyFlag applyFlag(m_isApplying);
            TryApplyFromElement(frameworkElement, m_settings);
        } catch (const winrt::hresult_error& ex) {
            Log(L"OnVisualTreeChange failed: 0x%08X %s", static_cast<unsigned>(ex.code()), ex.message().c_str());
        } catch (...) {
            Log(L"OnVisualTreeChange failed with an unknown exception");
        }

        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE OnElementStateChanged(
        InstanceHandle,
        VisualElementState,
        LPCWSTR) noexcept override {
        return S_OK;
    }

private:
    struct ScopedApplyFlag {
        explicit ScopedApplyFlag(bool& value) : flag(value) {
            flag = true;
        }

        ~ScopedApplyFlag() {
            flag = false;
        }

        bool& flag;
    };

    bool ShouldApplyNow() {
        if (m_isApplying) {
            return false;
        }

        const ULONGLONG now = GetTickCount64();
        if (m_lastApplyTick != 0 && now - m_lastApplyTick < 120) {
            return false;
        }

        m_lastApplyTick = now;
        return true;
    }

    template <typename T>
    T FromHandle(InstanceHandle handle) {
        if (!m_diagnostics || !handle) {
            return nullptr;
        }

        wf::IInspectable inspectable{nullptr};
        if (FAILED(m_diagnostics->GetIInspectableFromHandle(
                handle,
                reinterpret_cast<::IInspectable**>(winrt::put_abi(inspectable))))) {
            return nullptr;
        }

        return inspectable.try_as<T>();
    }

    winrt::com_ptr<IXamlDiagnostics> m_diagnostics;
    HookSettings m_settings;
    bool m_isApplying = false;
    ULONGLONG m_lastApplyTick = 0;
};

struct TrayWrapTap : winrt::implements<TrayWrapTap, IObjectWithSite> {
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) noexcept override {
        try {
            Log(L"TAP SetSite called: site=%p", site);

            if (m_visualTreeService && m_watcher) {
                m_visualTreeService->UnadviseVisualTreeChange(m_watcher.get());
            }

            m_visualTreeService = nullptr;
            m_diagnostics = nullptr;
            m_watcher = nullptr;

            if (!site) {
                Log(L"TAP site cleared");
                return S_OK;
            }

            m_visualTreeService = QueryInterfaceFrom<IVisualTreeService>(site);
            m_diagnostics = QueryInterfaceFrom<IXamlDiagnostics>(site);
            if (!m_visualTreeService || !m_diagnostics) {
                Log(L"TAP SetSite missing required XAML diagnostics interfaces");
                return E_NOINTERFACE;
            }

            const auto settings = LoadSettings(m_diagnostics.get());
            SetPromotionWorkerEnabled(!settings.reset, settings.statePath);
            m_watcher = winrt::make_self<VisualTreeWatcher>();
            m_watcher->Configure(m_diagnostics, settings);

            const HRESULT hr = m_visualTreeService->AdviseVisualTreeChange(m_watcher.get());
            if (FAILED(hr)) {
                Log(L"AdviseVisualTreeChange failed: 0x%08X", static_cast<unsigned>(hr));
                return hr;
            }

            Log(L"TAP attached: mode=%s rows=%d width=%d", settings.reset ? L"reset" : L"double", settings.rows, settings.width);
            return S_OK;
        } catch (const winrt::hresult_error& ex) {
            Log(L"SetSite failed: 0x%08X %s", static_cast<unsigned>(ex.code()), ex.message().c_str());
            return ex.code();
        } catch (...) {
            Log(L"SetSite failed with an unknown exception");
            return E_FAIL;
        }
    }

    HRESULT STDMETHODCALLTYPE GetSite(REFIID riid, void** site) noexcept override {
        if (!site) {
            return E_POINTER;
        }

        *site = nullptr;
        if (!m_diagnostics) {
            return E_FAIL;
        }

        return m_diagnostics->QueryInterface(riid, site);
    }

private:
    winrt::com_ptr<IVisualTreeService> m_visualTreeService;
    winrt::com_ptr<IXamlDiagnostics> m_diagnostics;
    winrt::com_ptr<VisualTreeWatcher> m_watcher;
};

struct TrayWrapTapFactory : winrt::implements<TrayWrapTapFactory, IClassFactory> {
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID riid, void** instance) noexcept override {
        Log(L"TAP factory CreateInstance called");

        if (!instance) {
            return E_POINTER;
        }

        *instance = nullptr;
        if (outer) {
            return CLASS_E_NOAGGREGATION;
        }

        auto tap = winrt::make_self<TrayWrapTap>();
        return tap->QueryInterface(riid, instance);
    }

    HRESULT STDMETHODCALLTYPE LockServer(BOOL) noexcept override {
        return S_OK;
    }
};

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID riid, void** factory) {
    Log(L"DllGetClassObject called");

    if (!factory) {
        return E_POINTER;
    }

    *factory = nullptr;
    if (!IsEqualCLSID(clsid, CLSID_TrayWrapDoublerTap)) {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    auto classFactory = winrt::make_self<TrayWrapTapFactory>();
    return classFactory->QueryInterface(riid, factory);
}

extern "C" HRESULT __stdcall DllCanUnloadNow() {
    return S_FALSE;
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(module);
        Log(L"TrayHook.Native loaded in PID %lu", GetCurrentProcessId());
    }

    return TRUE;
}
