using System.Windows;
using System.Windows.Controls;

namespace HardwareAuditToolkit.App.Views;

public partial class CpuStressView : UserControl
{
    public CpuStressView()
    {
        InitializeComponent();
        // Deliberate: NO auto-start — the operator starts the burn-in explicitly, so
        // the machine isn't loaded the moment the screen opens. This is the one
        // exception to the auto-start-on-load policy (owner decision); the keyboard,
        // mouse and monitor screens start their test on load.
    }
}
