using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DevKit.Models;
using DevKit.ViewModels;

namespace DevKit;

/// <summary>
/// 主窗口：左侧分类 + 右侧软件列表 + 底部操作与安装进度。
/// 交互：点击整行任意位置可切换勾选；点击复选框本身正常勾选。
/// </summary>
public partial class MainWindow : Window
{
    public MainViewModel MainVM { get; }
    public StoreViewModel StoreVM { get; }

    public MainWindow()
    {
        InitializeComponent();
        MainVM = new MainViewModel();
        StoreVM = new StoreViewModel();
        DataContext = MainVM;
        // 应用商店页加载时刷新状态
        Loaded += async (_, _) => await StoreVM.RefreshStatusAsync();
    }

    private void OnGridPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        if (src is null) return;

        // 点击在复选框内部 → 不拦截，交给复选框正常切换
        if (FindAncestor<CheckBox>(src) is not null) return;

        // 点击行内其他区域 → 阻止行选中，切换勾选
        if (ItemsControl.ContainerFromElement(ToolGrid, src) is DataGridRow row
            && row.Item is ToolDetection det)
        {
            e.Handled = true;
            det.IsSelected = !det.IsSelected;
        }
    }

    private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
