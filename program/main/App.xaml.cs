using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MainApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandled;
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report(e.Exception);
        e.Handled = true;
        Shutdown(1);
    }

    private void OnAppDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        Report(e.ExceptionObject as Exception);
    }

    private static void Report(Exception? ex)
    {
        string text = ex?.ToString() ?? "ошибка без исключения";
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup_error.txt"), text); } catch { }
        try { MessageBox.Show(text, "LERON CLI — ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
    }
}