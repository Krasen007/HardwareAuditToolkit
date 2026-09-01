using System.Windows.Controls;
using System.Windows.Input;
using HardwareAuditToolkit.App.ViewModels;

namespace HardwareAuditToolkit.App.Views
{
    /// <summary>
    /// Phase 4 mouse test screen. The click/scroll/drag capture is handled by the
    /// raw-input module on the event bus; this view only renders the log and hosts
    /// the tracing sub-screen. Tracing uses WPF pointer events on the canvas because
    /// it needs absolute coordinates in the canvas's own coordinate space (raw input
    /// only provides relative deltas) — the canvas lives inside a Viewbox so it
    /// scales visually while pointer positions stay in the fixed 600×360 space.
    /// </summary>
    public partial class MouseTestView : UserControl
    {
        public MouseTestView()
        {
            InitializeComponent();

            // Auto-start policy (owner decision): keyboard, mouse and monitor start
            // their test on load. See KeyboardTestView.xaml.cs for the reasoning.
            Loaded += (_, _) => ViewModel?.StartTestCommand.Execute(null);
        }

        private MouseTestModuleViewModel? ViewModel => DataContext as MouseTestModuleViewModel;

        private void TraceCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel is { } vm && sender is Canvas canvas)
            {
                var p = e.GetPosition(canvas);
                vm.StartTrace(p.X, p.Y);
                canvas.CaptureMouse();
            }
        }

        private void TraceCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (ViewModel is { } vm && sender is Canvas canvas)
            {
                var p = e.GetPosition(canvas);
                vm.AddTrace(p.X, p.Y);
            }
        }

        private void TraceCanvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (ViewModel is { } vm && sender is Canvas canvas)
            {
                canvas.ReleaseMouseCapture();
                vm.EndTrace();
            }
        }
    }
}
