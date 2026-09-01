using System.Windows;
using System.Windows.Controls;
using HardwareAuditToolkit.App.ViewModels;

namespace HardwareAuditToolkit.App.Views;

public partial class KeyboardTestView : UserControl
{
    public KeyboardTestView()
    {
        InitializeComponent();

        // Auto-start policy (roadmap Phase 2.6, one policy for all five modules):
        // every module whose run has a cost or a verdict — the four exclusive
        // tests — starts ONLY when the operator presses Start. Auto-start hides
        // "not run" from the operator and makes merely opening a screen look like
        // an audit. System Info is the sole exception: it is a read-only snapshot
        // with no verdict and no cost, so it collects when its screen opens.
        // Deliberate: NO auto-start here (mirrors CpuStressView).
    }
}
