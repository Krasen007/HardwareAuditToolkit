using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using HardwareAuditToolkit.App.Messages;
using HardwareAuditToolkit.App.Modules;
using HardwareAuditToolkit.App.Services;
using HardwareAuditToolkit.App.ViewModels;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HardwareAuditToolkit.App;

/// <summary>
/// Application bootstrap: single-instance enforcement (§9.3), the DI shell,
/// the shell/exit wiring for Phase 1 (§6), and the device-change listener (§9.5).
/// Nothing hardware-related starts until the instance check has passed.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private SingleInstanceEnforcer? _singleInstance;
    private ExitHotkeyService? _exitHotkey;
    private DeviceChangeService? _deviceChange;
    private bool _exitInitiated;

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

        // Build the shell graph. Shell ↔ Navigation is cyclic, so wire manually.
        var shell = _services.GetRequiredService<ShellViewModel>();
        var navigation = _services.GetRequiredService<INavigationService>();
        var deviceChange = _services.GetRequiredService<DeviceChangeService>();
        shell.Navigation = navigation;
        shell.DeviceChange = deviceChange;
        shell.ShowDashboard();

        var mainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.DataContext = shell;
        mainWindow.Closing += OnMainWindowClosing;
        mainWindow.Show();

        // §6 — every exit path (Ctrl+E, Exit button, native close) routes through
        // the orchestrator's exit flow. Subscribe on the app so the handler runs
        // regardless of which screen is active.
        WeakReferenceMessenger.Default.Register<ExitRequestedMessage>(this, (_, _) =>
            Dispatcher.BeginInvoke(HandleExitRequested));

        // §9.2 — Ctrl+E hook on its own dedicated thread.
        _exitHotkey = _services.GetRequiredService<ExitHotkeyService>();
        _exitHotkey.Start();

        // §9.5 — live device-change listener.
        _deviceChange = deviceChange;
        _deviceChange.Start();

        // Phase 2 — start best-effort sensor polling so the event bus carries
        // live thermal/load data from the first module that needs it.
        var sensors = _services.GetRequiredService<ISensorProvider>();
        sensors.Start();
    }

    /// <summary>
    /// Routed through the orchestrator so any running module is cancelled cleanly
    /// before the app closes (§4, §6).
    /// </summary>
    private void HandleExitRequested()
    {
        if (_exitInitiated)
        {
            return;
        }

        _exitInitiated = true;

        if (_services is null)
        {
            Shutdown();
            return;
        }

        var orchestrator = _services.GetRequiredService<TestOrchestrator>();
        orchestrator.CancelAll();

        var session = _services.GetRequiredService<AuditSession>();
        if (session.CompletedAt is null)
        {
            session.CompletedAt = DateTime.UtcNow;
        }

        Shutdown();
    }

    /// <summary>
    /// Native window close (X) routes through the same exit flow as Ctrl+E and
    /// the Exit button (§6). The guard prevents reentrancy once shutdown begins.
    /// </summary>
    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exitInitiated)
        {
            return;
        }

        e.Cancel = true;
        WeakReferenceMessenger.Default.Send(new ExitRequestedMessage());
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Phase 1 shell + services.
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<DeviceChangeService>();
        services.AddSingleton<ExitHotkeyService>();
        services.AddSingleton<INavigationService, NavigationService>();

        // Phase 2 — providers and modules.
        services.AddSingleton<SystemInfoProvider>();
        services.AddSingleton<ISensorProvider, LibreHardwareMonitorSensorProvider>();
        services.AddSingleton<SystemInfoModule>();
        services.AddSingleton<CpuStressModule>();
        // Register the modules as ITestModule so the orchestrator discovers them
        // via IEnumerable<ITestModule>; the concrete singletons guarantee the same
        // instance the view models drive.
        services.AddSingleton<ITestModule>(sp => sp.GetRequiredService<SystemInfoModule>());
        services.AddSingleton<ITestModule>(sp => sp.GetRequiredService<CpuStressModule>());

        // Phase 3 — keyboard test: raw input capture + the exclusive module.
        services.AddSingleton<IRawKeyboardInput, RawKeyboardInput>();
        services.AddSingleton<KeyboardTestModule>();
        services.AddSingleton<ITestModule>(sp => sp.GetRequiredService<KeyboardTestModule>());

        // Phase 4 — mouse test: raw input capture + the exclusive module.
        services.AddSingleton<IRawMouseInput, RawMouseInput>();
        services.AddSingleton<MouseTestModule>();
        services.AddSingleton<ITestModule>(sp => sp.GetRequiredService<MouseTestModule>());

        // Phase 2 — module screen view models. NavigationService resolves these per
        // navigation, and each instance registers event-bus subscriptions in its
        // constructor and is disposed when its screen is left (NavigationService
        // SetScreen), so they must be transient, not singletons: a singleton would
        // stay unsubscribed after the first disposal on subsequent visits.
        services.AddTransient<SystemInfoModuleViewModel>();
        services.AddTransient<CpuStressModuleViewModel>();
        services.AddTransient<KeyboardTestModuleViewModel>();
        services.AddTransient<MouseTestModuleViewModel>();

        // Core orchestrator owns the module set and the session.
        var session = new AuditSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Hostname = Environment.MachineName,
            StartedAt = DateTime.UtcNow,
        };
        services.AddSingleton(session);
        services.AddSingleton<TestOrchestrator>();

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
