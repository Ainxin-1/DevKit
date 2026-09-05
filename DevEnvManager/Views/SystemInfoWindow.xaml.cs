using System.Windows;
using DevEnvManager.Core;

namespace DevEnvManager.Views;

/// <summary>系统环境信息窗口</summary>
public partial class SystemInfoWindow : Window
{
    public SystemInfoWindow()
    {
        InitializeComponent();
        InfoGrid.ItemsSource = SystemInfoProvider.Collect();
    }
}
