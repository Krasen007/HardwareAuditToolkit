using System.Windows;
using System.Windows.Controls;
using HardwareAuditToolkit.App.ViewModels;
using HardwareAuditToolkit.Infrastructure;

namespace HardwareAuditToolkit.App.Views;

/// <summary>
/// Phase 5 monitor test controls (architecture §10 Phase 5). Lists the live
/// display topology and DDC/CI brightness, and launches the fullscreen pattern
/// window. The brightness slider pushes to the view model via its ValueChanged
/// handler; the value is one-way bound back so programmatic DDC refreshes don't
/// echo back to the hardware.
/// </summary>
public partial class MonitorTestView : UserControl
{
    public MonitorTestView()
    {
        InitializeComponent();

        // Auto-start policy (owner decision): keyboard, mouse and monitor start
        // their test on load; leaving is a non-event so this can never stamp the
        // report. See KeyboardTestView.xaml.cs for the reasoning.
        Loaded += (_, _) => ViewModel?.StartTestCommand.Execute(null);
    }

    private MonitorTestModuleViewModel? ViewModel => DataContext as MonitorTestModuleViewModel;

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ViewModel?.ApplyBrightness((int)e.NewValue);
    }
}
