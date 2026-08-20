using System.Windows.Controls;
using CommunityToolkit.Mvvm.Messaging;
using HardwareAuditToolkit.Core.Messages;

namespace HardwareAuditToolkit.App.Views;

/// <summary>
/// Reusable "Exit Test" overlay (§6). Self-contained: its button raises
/// <see cref="ExitRequestedMessage"/> on the event bus, so it works on any
/// screen including fullscreen monitor tests where native chrome is hidden.
/// Drop it into a screen's top-right corner; it is auto-hiding in later phases.
/// </summary>
public partial class ExitOverlay : UserControl
{
    public ExitOverlay()
    {
        InitializeComponent();
    }

    private void ExitButton_Click(object sender, System.Windows.RoutedEventArgs e)
        => WeakReferenceMessenger.Default.Send(new ExitRequestedMessage());
}
