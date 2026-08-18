using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareAuditToolkit.App.Services;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// Placeholder screen for a module that is not yet implemented (Phases 2–5).
/// Demonstrates that every screen carries its own exit/mouse paths and a way
/// back to the dashboard (§6).
/// </summary>
public sealed partial class ModulePlaceholderViewModel : ObservableObject
{
    private readonly INavigationService _navigation;

    public string ModuleId { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public bool IsExclusive { get; }

    [RelayCommand]
    private void Back()
        => _navigation.NavigateToDashboard();

    public ModulePlaceholderViewModel(string moduleId, INavigationService navigation)
    {
        _navigation = navigation;
        ModuleId = moduleId;

        var known = new Dictionary<string, (string Name, string Desc, bool Exclusive)>(StringComparer.OrdinalIgnoreCase)
        {
            ["keyboard"] = ("Keyboard Test", "Per-key coverage, WPM and accuracy.", true),
            ["mouse"] = ("Mouse Test", "Click/scroll/drag log and tracing accuracy.", true),
            ["monitor"] = ("Monitor Test", "Fullscreen patterns and DDC/CI brightness.", true),
            ["system"] = ("System Info", "WMI/CIM inventory: CPU, RAM, disk, BIOS.", false),
            ["stress"] = ("CPU Stress Test", "Fixed-duration burn-in across all cores.", true),
        };

        if (known.TryGetValue(moduleId, out var info))
        {
            DisplayName = info.Name;
            Description = info.Desc;
            IsExclusive = info.Exclusive;
        }
        else
        {
            DisplayName = moduleId;
            Description = "Module not yet implemented.";
            IsExclusive = false;
        }
    }
}
