using System.Windows;
using System.Windows.Controls;
using HardwareAuditToolkit.App.ViewModels;

namespace HardwareAuditToolkit.App.Views;

public partial class CpuStressView : UserControl
{
    public CpuStressView()
    {
        InitializeComponent();
        Loaded += (_, _) => (DataContext as CpuStressModuleViewModel)?.StartTestCommand.Execute(null);
    }
}
