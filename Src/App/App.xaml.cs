using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using HardwareAuditToolkit.Core.Messages;
using HardwareAuditToolkit.Core.Modules;
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
    private readonly IDiagnosticLog _diag = new FileDiagnosticLog();
    private bool _exitInitiated;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Phase 7 — last-resort fault containment: a single failing call must
        // degrade to "unavailable" rather than crash the audit (architecture §9.7).
        WireGlobalFaultHandlers(_diag);

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
        shell.ReportExport = _services.GetRequiredService<ReportExportService>();
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
    /// Phase 7 — last-resort fault containment. Background run loops are guarded at
    /// their source; here we keep the WPF/UI thread alive on an unhandled exception
    /// and log every other failure so a fault is never silent (architecture §9.7).
    /// </summary>
    private static void WireGlobalFaultHandlers(IDiagnosticLog log)
    {
        Application.Current.DispatcherUnhandledException += (_, e) =>
        {
            log.Write($"unhandled UI exception (app kept alive) — {e.Exception}");
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.Write($"unhandled exception — {e.ExceptionObject}");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Write($"unobserved task exception — {e.Exception}");
            e.SetObserved();
        };
    }

    /// <summary>
    /// Global exit paths (Ctrl+E, Exit Test button) cancel any running module
    /// and return to the dashboard (§6). Only the native window close (X) quits
    /// the application.
    /// </summary>
    private void HandleExitRequested()
    {
        if (_services is null)
        {
            Shutdown();
            return;
        }

        var orchestrator = _services.GetRequiredService<TestOrchestrator>();
        var navigation = _services.GetRequiredService<INavigationService>();

        if (orchestrator.CurrentExclusiveModule is not null || orchestrator.RunningModules.Count > 0)
        {
            orchestrator.CancelAll();
        }

        navigation.NavigateToDashboard();
    }

    /// <summary>
    /// Native window close (X) is the actual app-quit path (§6). It stops any
    /// running module <em>without recording</em> (roadmap Phase 2: on shutdown the
    /// report is never read, so a close is a non-event, not an abort), stamps the
    /// session, and shuts down.
    /// </summary>
    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exitInitiated)
        {
            return;
        }

        _exitInitiated = true;
        e.Cancel = true;

        var orchestrator = _services?.GetRequiredService<TestOrchestrator>();
        orchestrator?.StopAll();

        var session = _services?.GetRequiredService<AuditSession>();
        if (session is not null && session.CompletedAt is null)
        {
            session.CompletedAt = DateTime.UtcNow;
        }

        Shutdown();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Phase 1 shell + services.
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<DeviceChangeService>();
        services.AddSingleton<ExitHotkeyService>();
        // Phase 7 — one shared, file-backed diagnostics sink for all fault-guard paths.
        services.AddSingleton<IDiagnosticLog, FileDiagnosticLog>();
        services.AddSingleton<INavigationService, NavigationService>();

        // Roadmap E1 — the single routing table: module id → screen view-model
        // factory. The dashboard cards are generated from the same modules'
        // IModuleMetadata, so a new module registers one entry here (plus its
        // DI/VM/DataTemplate entries) instead of editing a dashboard list and a
        // navigation switch.
        services.AddSingleton(new ModuleScreenRegistry(
        [
            new KeyValuePair<string, Func<IServiceProvider, object>>("system", sp => sp.GetRequiredService<SystemInfoModuleViewModel>()),
            new KeyValuePair<string, Func<IServiceProvider, object>>("stress", sp => sp.GetRequiredService<CpuStressModuleViewModel>()),
            new KeyValuePair<string, Func<IServiceProvider, object>>("keyboard", sp => sp.GetRequiredService<KeyboardTestModuleViewModel>()),
            new KeyValuePair<string, Func<IServiceProvider, object>>("mouse", sp => sp.GetRequiredService<MouseTestModuleViewModel>()),
            new KeyValuePair<string, Func<IServiceProvider, object>>("monitor", sp => sp.GetRequiredService<MonitorTestModuleViewModel>()),
        ]));

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

        // Phase 5 — monitor test: DDC/CI control + the exclusive module.
        services.AddSingleton<IDdcCiControl, DdcCiControl>();
        services.AddSingleton<MonitorTestModule>();
        services.AddSingleton<ITestModule>(sp => sp.GetRequiredService<MonitorTestModule>());

        // Phase 6 — dashboard view model (resolved via DI so its ReportExportService
        // dependency is satisfied when NavigationService navigates to it).
        services.AddTransient<DashboardViewModel>();

        // Phase 2 — module screen view models. NavigationService resolves these per
        // navigation, and each instance registers event-bus subscriptions in its
        // constructor and is disposed when its screen is left (NavigationService
        // SetScreen), so they must be transient, not singletons: a singleton would
        // stay unsubscribed after the first disposal on subsequent visits.
        services.AddTransient<SystemInfoModuleViewModel>();
        services.AddTransient<CpuStressModuleViewModel>();
        services.AddTransient<KeyboardTestModuleViewModel>();
        services.AddTransient<MouseTestModuleViewModel>();
        services.AddTransient<MonitorTestModuleViewModel>();

        // Core orchestrator owns the module set and the session.
        var session = new AuditSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Hostname = Environment.MachineName,
            StartedAt = DateTime.UtcNow,
        };
        services.AddSingleton(session);
        services.AddSingleton<TestOrchestrator>();

        // Phase 6 — reporting: pure exporter (Core) + WPF-bound export service (App).
        // The service receives the full module roster so the exported report can name
        // modules that were never started (roadmap C2) instead of silently omitting them.
        services.AddSingleton<Core.Reporting.SessionExporter>();
        services.AddSingleton<ReportExportService>(sp =>
            new ReportExportService(
                sp.GetRequiredService<Core.Reporting.SessionExporter>(),
                sp.GetRequiredService<AuditSession>(),
                sp.GetServices<ITestModule>()));

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
