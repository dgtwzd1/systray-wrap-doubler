namespace SystrayWrapDoubler;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var command = AppCommand.Parse(args);
        if (command is not null)
        {
            return command.Execute();
        }

        ApplicationConfiguration.Initialize();
        using var context = new TrayAppContext();
        Application.Run(context);
        return 0;
    }
}
