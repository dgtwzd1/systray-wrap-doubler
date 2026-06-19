namespace SystrayWrapDoubler;

internal static class AppIcon
{
    public static Icon Load()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico");
        if (File.Exists(iconPath))
        {
            return new Icon(iconPath);
        }

        return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
    }
}
