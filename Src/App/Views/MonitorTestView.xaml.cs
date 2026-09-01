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

        // Auto-start policy (roadmap Phase 2.6, one policy for all five modules):
        // every module whose run has a cost or a verdict — the four exclusive
        // tests — starts ONLY when the operator presses Start. Auto-start here was
        // actively harmful: opening the screen and leaving used to stamp the
        // report. See KeyboardTestView.xaml.cs for the reasoning. Deliberate:
        // NO auto-start.
    }

    private MonitorTestModuleViewModel? ViewModel => DataContext as MonitorTestModuleViewModel;

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ViewModel?.ApplyBrightness((int)e.NewValue);
    }
}
