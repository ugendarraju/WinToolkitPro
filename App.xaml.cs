using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace WinToolkit;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        File.AppendAllText("crash.log", $"[{DateTime.Now}] Dispatcher Unhandled Exception: {e.Exception}\n");
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        File.AppendAllText("crash.log", $"[{DateTime.Now}] Domain Unhandled Exception: {e.ExceptionObject}\n");
    }
}
