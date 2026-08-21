using System.Windows;
using System.Windows.Controls;

namespace HardwareAuditToolkit.App.Views;

public partial class CpuStressView : UserControl
{
    public CpuStressView()
    {
        InitializeComponent();
        // Deliberate: NO auto-start (Phase 2 improvement) — the operator starts the
        // burn-in explicitly, so the machine isn't loaded the moment the screen opens.
    }
}
