using System.Windows;
using System.Windows.Controls;
using HardwareAuditToolkit.App.ViewModels;

namespace HardwareAuditToolkit.App.Views;

public partial class KeyboardTestView : UserControl
{
    public KeyboardTestView()
    {
        InitializeComponent();
        Loaded += (_, _) => (DataContext as KeyboardTestModuleViewModel)?.StartTestCommand.Execute(null);
    }
}
