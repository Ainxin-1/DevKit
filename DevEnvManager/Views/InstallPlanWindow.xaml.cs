using System.Windows;
using DevEnvManager.Core;

namespace DevEnvManager.Views;

/// <summary>安装计划确认窗口（安全要求：安装前显示安装计划）</summary>
public partial class InstallPlanWindow : Window
{
    public InstallPlanWindow(List<InstallPlanItem> plan)
    {
        InitializeComponent();
        var rows = plan.Select((item, i) => new PlanRow
        {
            Order = (i + 1).ToString(),
            ToolName = item.Tool.Name,
            Kind = item.Tool.CategoryEnum switch
            {
                Models.ToolCategory.PackageManager => "包管理器",
                _ => "开发环境"
            },
            Method = item.Tool.Install?.EffectiveMethods.FirstOrDefault()?.MethodEnum switch
            {
                Models.InstallMethod.Winget => "winget",
                Models.InstallMethod.Scoop => "scoop",
                Models.InstallMethod.Bundled => "随宿主",
                Models.InstallMethod.Official => "官方命令",
                _ => "-"
            },
            Reason = item.IsUserSelected ? "用户选择" : item.Reason
        });
        PlanGrid.ItemsSource = rows;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private class PlanRow
    {
        public string Order { get; init; } = "";
        public string ToolName { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Method { get; init; } = "";
        public string Reason { get; init; } = "";
    }
}
