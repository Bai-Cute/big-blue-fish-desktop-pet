namespace BigBlueFish.Uninstaller;
internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => UninstallApplication.Run(args);
}
