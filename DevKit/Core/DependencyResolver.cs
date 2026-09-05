using DevKit.Models;

namespace DevKit.Core;

/// <summary>安装计划中的一项</summary>
public class InstallPlanItem
{
    public required ToolInfo Tool { get; init; }

    /// <summary>true=用户主动选择；false=作为依赖自动加入</summary>
    public bool IsUserSelected { get; init; }

    /// <summary>通过哪个依赖链引入（便于展示）</summary>
    public string Reason { get; init; } = "";
}

/// <summary>循环依赖检测结果</summary>
public class DependencyCycleError
{
    public List<string> CyclePath { get; init; } = new();
    public override string ToString() => "循环依赖: " + string.Join(" -> ", CyclePath);
}

/// <summary>
/// 依赖分析器：根据用户勾选，展开依赖、去重、按依赖优先排序。
/// 使用 DFS 三态（Unvisited/Visiting/Visited）检测循环依赖。
/// 已安装的依赖不会重复安装。
/// </summary>
public class DependencyResolver
{
    private enum DfsState { Unvisited, Visiting, Visited }

    /// <summary>
    /// 生成安装计划。
    /// </summary>
    /// <param name="selected">用户勾选的软件</param>
    /// <param name="allTools">全部软件配置（按名称索引）</param>
    /// <param name="isInstalled">判断某软件是否已安装</param>
    /// <param name="cycles">输出：检测到的循环依赖列表</param>
    public static List<InstallPlanItem> BuildPlan(
        IEnumerable<ToolInfo> selected,
        IReadOnlyDictionary<string, ToolInfo> allTools,
        Func<string, bool> isInstalled,
        out List<DependencyCycleError> cycles)
    {
        var plan = new List<InstallPlanItem>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var state = new Dictionary<string, DfsState>(StringComparer.OrdinalIgnoreCase);
        var cycleList = new List<DependencyCycleError>();
        var currentPath = new List<string>();

        void Visit(ToolInfo tool, string reason, bool userSelected)
        {
            var name = tool.Name;
            if (state.TryGetValue(name, out var st))
            {
                if (st == DfsState.Visiting)
                {
                    // 发现循环依赖
                    var cycleStart = currentPath.IndexOf(name);
                    var cyclePath = cycleStart >= 0
                        ? currentPath.Skip(cycleStart).ToList()
                        : new List<string> { name };
                    cyclePath.Add(name); // 闭合
                    var cycle = new DependencyCycleError { CyclePath = cyclePath };
                    if (!cycleList.Any(c => c.CyclePath.SequenceEqual(cyclePath)))
                    {
                        cycleList.Add(cycle);
                        Logger.Error($"检测到循环依赖: {cycle}");
                    }
                    return;
                }
                if (st == DfsState.Visited) return;
            }

            state[name] = DfsState.Visiting;
            currentPath.Add(name);

            // 先解析依赖（深度优先，依赖在前）
            foreach (var depName in tool.Dependencies)
            {
                if (!allTools.TryGetValue(depName, out var dep))
                {
                    Logger.Warn($"依赖 {depName} 不在工具列表中（被 {name} 引用）");
                    continue;
                }

                if (!isInstalled(dep.Name))
                {
                    Visit(dep, $"{name} 依赖 {dep.Name}", false);
                    if (added.Add(dep.Name))
                    {
                        plan.Add(new InstallPlanItem
                        {
                            Tool = dep,
                            IsUserSelected = false,
                            Reason = $"{name} 依赖 {dep.Name}"
                        });
                    }
                }
                else
                {
                    Logger.Info($"依赖 {dep.Name} 已安装，跳过");
                }
            }

            currentPath.RemoveAt(currentPath.Count - 1);
            state[name] = DfsState.Visited;

            if (userSelected && !isInstalled(tool.Name) && added.Add(tool.Name))
            {
                plan.Add(new InstallPlanItem { Tool = tool, IsUserSelected = true, Reason = "用户选择" });
            }
        }

        foreach (var tool in selected)
        {
            Visit(tool, "", userSelected: true);
        }

        cycles = cycleList;
        return plan;
    }

    /// <summary>旧版兼容：不输出 cycles</summary>
    public static List<InstallPlanItem> BuildPlan(
        IEnumerable<ToolInfo> selected,
        IReadOnlyDictionary<string, ToolInfo> allTools,
        Func<string, bool> isInstalled)
        => BuildPlan(selected, allTools, isInstalled, out _);
}
