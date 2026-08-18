using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace HardwareAuditToolkit.App;

/// <summary>
/// Application bootstrap: single-instance enforcement (§9.3), the DI shell,
/// and the main window. Nothing hardware-related starts until the instance
/// check has passed.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private SingleInstanceEnforcer? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // §9.1 — point the single-file extraction directory at a predictable
        // path so security teams can allow-list one location. Best-effort,
        // idempotent, and takes effect from the next launch onward.
        BundleExtractionBootstrap.EnsureExtractionDirectoryRedirected();

        // §9.3 — single-instance enforcement before anything else: no hooks,
        // no worker threads, no windows.
        _singleInstance = new SingleInstanceEnforcer();
        if (!_singleInstance.TryAcquire())
        {
            SingleInstanceEnforcer.SignalFirstInstance();
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _singleInstance.ActivateRequested += OnActivateRequested;

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        var mainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Phase 1 registers the TestOrchestrator, the module set, and the
        // messenger-based event bus here.
        services.AddSingleton<MainWindow>();
    }

    /// <summary>
    /// Runs on the first instance when a second launch is detected: restores
    /// and foregrounds the existing main window (§9.3).
    /// </summary>
    private void OnActivateRequested()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is not Window window)
            {
                return;
            }

            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Show();
            window.Activate();
            window.Focus();

            // Toggle Topmost to force the window above others even when
            // SetForegroundWindow would otherwise be restricted.
            window.Topmost = true;
            window.Topmost = false;
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
