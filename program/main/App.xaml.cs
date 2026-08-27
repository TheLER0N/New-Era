using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MainApp;

public partial class App : Application
{
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandled;
        DispatcherUnhandledException += OnDispatcherUnhandled;
    }

    private void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Report(e.Exception);
        Shutdown(1);
    }

    private void OnAppDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        Report(e.ExceptionObject as Exception);
    }

    // in-process gateway (порт 51234) и WebView2 держат свои потоки даже после
    // закрытия всех окон — из-за этого LeronCli.exe оставался в диспетчере задач.
    // Environment.Exit гарантированно завершает процесс при любом выходе.
    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        Environment.Exit(e.ApplicationExitCode);
    }

    private static void Report(Exception? ex)
    {
        string text = ex?.ToString() ?? "ошибка без исключения";
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup_error.txt"), text); } catch { }
        try { MessageBox.Show(text, "LERON CLI — ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
    }
}