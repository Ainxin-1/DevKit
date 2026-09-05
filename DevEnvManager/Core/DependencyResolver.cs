using DevEnvManager.Models;

namespace DevEnvManager.Core;

/// <summary>安装计划中的一项</summary>
public class InstallPlanItem
{
    public required ToolInfo Tool { get; init; }

    /// <summary>true=用户主动选择；false=作为依赖自动加入</summary>
    public bool IsUserSelected { get; init; }

    /// <summary>通过哪个依赖链引入（便于展示）</summary>
    public string Reason { get; init; } = "";
}

/// <summary>
/// 依赖分析器：根据用户勾选，展开依赖、去重、按依赖优先排序。
/// 已安装的依赖不会重复安装。
/// </summary>
public class DependencyResolver
{
    /// <summary>
    /// 生成安装计划。
    /// </summary>
    /// <param name="selected">用户勾选的软件</param>
    /// <param name="allTools">全部软件配置（按名称索引）</param>
    /// <param name="isInstalled">判断某软件是否已安装</param>
    public static List<InstallPlanItem> BuildPlan(
        IEnumerable<ToolInfo> selected,
        IReadOnlyDictionary<string, ToolInfo> allTools,
        Func<string, bool> isInstalled)
    {
        var plan = new List<InstallPlanItem>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddDependencies(ToolInfo tool, string reason, bool userSelected)
        {
            // 先解析依赖（深度优先，依赖在前）
            foreach (var depName in tool.Dependencies)
            {
                if (!allTools.TryGetValue(depName, out var dep)) continue;
                if (!visited.Add(dep.Name)) continue;   // 环保护

                if (!isInstalled(dep.Name))
                {
                    AddDependencies(dep, $"依赖: {tool.Name} 需要 {dep.Name}", false);
                    if (added.Add(dep.Name))
                    {
                        plan.Add(new InstallPlanItem { Tool = dep, IsUserSelected = false, Reason = $"{tool.Name} 依赖 {dep.Name}" });
                    }
                }
                else
                {
                    Logger.Info($"依赖 {dep.Name} 已安装，跳过");
                }
            }

            if (userSelected && !isInstalled(tool.Name) && added.Add(tool.Name))
            {
                plan.Add(new InstallPlanItem { Tool = tool, IsUserSelected = true, Reason = "用户选择" });
            }
        }

        foreach (var tool in selected)
        {
            visited.Clear();
            AddDependencies(tool, "", userSelected: true);
        }
        return plan;
    }
}
