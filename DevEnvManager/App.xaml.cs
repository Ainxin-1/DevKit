using System.Windows;
using System.Windows.Threading;
using DevEnvManager.Core;

namespace DevEnvManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // UI 线程未处理异常
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Error($"[UI未处理异常] {args.Exception}");
            args.Handled = true; // 不让程序直接崩溃，只写日志
        };

        // 非 UI 线程未处理异常
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Logger.Error($"[非UI未处理异常] IsTerminating={args.IsTerminating} {ex}");
        };

        // Task 未观察异常
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error($"[Task未观察异常] {args.Exception}");
            args.SetObserved();
        };

        base.OnStartup(e);
    }
}
