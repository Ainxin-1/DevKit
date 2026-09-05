using System.Windows;
using DevKit.Core;

namespace DevKit.Views;

/// <summary>系统环境信息窗口</summary>
public partial class SystemInfoWindow : Window
{
    public SystemInfoWindow()
    {
        InitializeComponent();
        InfoGrid.ItemsSource = SystemInfoProvider.Collect();
    }
}
