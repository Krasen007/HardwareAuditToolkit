using System.Windows;
using System.Windows.Controls;
using HardwareAuditToolkit.App.ViewModels;

namespace HardwareAuditToolkit.App.Views;

public partial class KeyboardTestView : UserControl
{
    public KeyboardTestView()
    {
        InitializeComponent();

        // Auto-start policy (owner decision, roadmap Phase 2.6): the keyboard, mouse
        // and monitor screens START THEIR TEST ON LOAD — the operator opens a screen
        // to test that device, and capture must be live immediately. Leaving is a
        // non-event (StopModule in the view model's Dispose), so auto-start can never
        // pollute the report. CPU Stress keeps an explicit Start (loading the machine
        // the moment the screen opens is not acceptable), and System Info collects in
        // its view-model constructor.
        Loaded += (_, _) => (DataContext as KeyboardTestModuleViewModel)?.StartTestCommand.Execute(null);
    }
}
