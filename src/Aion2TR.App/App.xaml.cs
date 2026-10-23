using System;
using System.Threading;
using System.Windows;
using Aion2TR.App.Services;

namespace Aion2TR.App;

public partial class App : Application
{
    private static Mutex? _mutex;
    private const string MutexName = "Global\\Aion2TR_Patch_Launcher_Instance";

    protected override void OnStartup(StartupEventArgs e)
    {
        bool createdNew;
        _mutex = new Mutex(true, MutexName, out createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "AION2TR uygulaması zaten arka planda çalışıyor.",
                "AION2TR",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Shutdown();
            return;
        }

        AppLogger.Info("==================================================");
        AppLogger.Info("AION2TR Application Started (v1.0.1)");
        AppLogger.Info("==================================================");

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            AppLogger.Error("AppDomain Unhandled Exception", args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            AppLogger.Error("Dispatcher Unhandled Exception", args.Exception);
            MessageBox.Show(
                "Beklenmeyen bir hata oluştu.\nOyun dosyalarına zarar verilmedi.\n\nAyrıntılar günlük dosyasına kaydedildi.",
                "AION2TR Hata",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("AION2TR Application Exiting");
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
