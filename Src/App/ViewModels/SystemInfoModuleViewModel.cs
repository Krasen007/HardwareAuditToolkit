using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HardwareAuditToolkit.App.Services;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Infrastructure;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// View model for the Phase 2 System Info module (architecture §10). Shows the
/// static WMI/CIM inventory plus a live sensor readout fed by
/// <see cref="SensorReadingsMessage"/> on the event bus. The module is started
/// through the orchestrator so its findings are recorded in the session.
/// </summary>
public sealed partial class SystemInfoModuleViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigation;
    private readonly SystemInfoProvider _provider;
    private readonly TestOrchestrator _orchestrator;
    private readonly Dispatcher _dispatcher;

    [ObservableProperty]
    private ObservableCollection<InfoRow> _inventory = new();

    [ObservableProperty]
    private ObservableCollection<SensorRow> _sensors = new();

    [ObservableProperty]
    private string _statusText = "Collecting inventory…";

    [ObservableProperty]
    private int _sensorCount;

    public SystemInfoModuleViewModel(
        INavigationService navigation,
        SystemInfoProvider provider,
        TestOrchestrator orchestrator)
    {
        _navigation = navigation;
        _provider = provider;
        _orchestrator = orchestrator;
        _dispatcher = Application.Current.Dispatcher;

        _orchestrator.TryStartModule("system", out _);

        LoadInventory();
        WeakReferenceMessenger.Default.Register<SensorReadingsMessage>(this, OnSensors);
    }

    private void LoadInventory()
    {
        try
        {
            var s = _provider.GetSnapshot();
            var rows = new List<InfoRow>
            {
                new("Hostname", Environment.MachineName, "System"),
                new("Operating system", s.OperatingSystem, "System"),
                new("OS architecture", s.OsArchitecture, "System"),
                new("System", string.Join(" ", new[] { s.SystemManufacturer, s.SystemModel }.Where(x => !string.IsNullOrWhiteSpace(x))), "System"),
                new("CPU", s.CpuName, "CPU"),
                new("Physical cores", s.PhysicalCores?.ToString(), "CPU"),
                new("Logical processors", s.LogicalProcessors?.ToString(), "CPU"),
                new("Max clock", s.MaxClockSpeedMhz is { } m ? $"{m} MHz" : null, "CPU"),
                new("Total RAM", s.TotalRamFormatted, "Memory"),
                new("Motherboard", s.Motherboard, "Mainboard"),
                new("BIOS", s.BiosVersion, "Mainboard"),
                new("BIOS manufacturer", s.BiosManufacturer, "Mainboard"),
            };

            foreach (var disk in s.Disks)
            {
                rows.Add(new InfoRow("Disk", disk, "Storage"));
            }

            Inventory = new ObservableCollection<InfoRow>(rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)));
            StatusText = "Inventory ready.";
        }
        catch (Exception ex)
        {
            StatusText = $"Inventory error: {ex.Message}";
        }
    }

    private void OnSensors(object? _, SensorReadingsMessage message)
    {
        // Sensor messages arrive on the provider's background timer thread; marshal
        // to the UI thread before touching the observable collection.
        var readings = message.Readings;
        _dispatcher.Invoke(() =>
        {
            Sensors = new ObservableCollection<SensorRow>(
                readings.Select(r => new SensorRow(r.HardwareName, r.SensorName,
                    r.Value is { } v ? $"{v:0.0} {r.Unit}".Trim() : "N/A")));
            SensorCount = Sensors.Count;
            if (Sensors.Count == 0 && StatusText == "Inventory ready.")
            {
                StatusText = "Inventory ready. (No live sensors available without elevation.)";
            }
        });
    }

    [RelayCommand]
    private void Back()
        => _navigation.NavigateToDashboard();

    public void Dispose()
        => WeakReferenceMessenger.Default.Unregister<SensorReadingsMessage>(this);
}
