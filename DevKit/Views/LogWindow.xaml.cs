using System.Diagnostics;
using System.Windows;
using DevKit.Core;

namespace DevKit.Views;

/// <summary>日志查看窗口（实时展示内存日志，可打开日志文件）</summary>
public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
        LogList.ItemsSource = Logger.Entries;
        LogList.ScrollIntoView(Logger.Entries.Count > 0 ? Logger.Entries[^1] : null);
    }

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Logger.LogFile) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开日志文件：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
