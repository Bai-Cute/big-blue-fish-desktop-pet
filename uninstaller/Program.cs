namespace BigBlueFish.Uninstaller;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        return UninstallApplication.Run(args);
    }
}
