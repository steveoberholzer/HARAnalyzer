using System.Windows;

namespace HARAnalyzer;

public partial class App : Application
{
    private void App_Startup(object sender, StartupEventArgs e)
    {
        var mainWindow = new MainWindow();
        mainWindow.Show();

        // If a .har file is passed as a command-line argument, open it
        if (e.Args.Length > 0 && e.Args[0].EndsWith(".har", StringComparison.OrdinalIgnoreCase))
            mainWindow.OpenFileOnStartup(e.Args[0]);
    }
}
