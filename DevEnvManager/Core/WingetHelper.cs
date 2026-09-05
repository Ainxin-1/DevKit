using System.Text.RegularExpressions;

namespace DevEnvManager.Core;

/// <summary>
/// winget 封装：检测可用性、查询包 ID、安装、读取已安装列表。
/// 安装命令全部经过 CommandRunner（写入日志）。
/// </summary>
public class WingetHelper
{
    public const string WingetExe = "winget";

    /// <summary>winget 是否可用</summary>
    public static bool IsAvailable()
    {
        var r = CommandRunner.Run(WingetExe, "--version", timeoutMs: 15_000);
        return r.Succeeded && r.Output.Contains('.');
    }

    /// <summary>获取 winget 版本（不可用返回 null）</summary>
    public static string? GetVersion()
    {
        var r = CommandRunner.Run(WingetExe, "--version", timeoutMs: 15_000);
        return r.Succeeded ? r.Output.Trim() : null;
    }

    /// <summary>单个包信息（查询确认包 ID 是否存在）</summary>
    public class PackageInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Source { get; set; } = "";
    }

    /// <summary>
    /// 查询确认包 ID 是否真实存在（安全要求：不盲目使用写死的 ID）。
    /// </summary>
    public static bool PackageExists(string id)
    {
        var r = CommandRunner.Run(WingetExe, $"show -e --id \"{id}\" --accept-source-agreements --disable-interactivity", timeoutMs: 60_000);
        return r.Succeeded;
    }

    /// <summary>
    /// 搜索软件包，返回候选列表（Id / Name / Version / Source）。
    /// 用于包 ID 不存在时帮助用户找到正确 ID。
    /// </summary>
    public static List<PackageInfo> Search(string query, int max = 10)
    {
        var list = new List<PackageInfo>();
        var r = CommandRunner.Run(
            WingetExe,
            $"search \"{query}\" --accept-source-agreements --disable-interactivity",
            timeoutMs: 60_000);
        if (!r.Succeeded) return list;

        foreach (var line in r.Output.Split('\n'))
        {
            var row = ParseTableLine(line);
            if (row is null || row.Count < 2) continue;
            // 跳过表头
            if (row[0].Equals("名称", StringComparison.OrdinalIgnoreCase)
                || row[0].Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
            if (row[1].Equals("Id", StringComparison.OrdinalIgnoreCase)) continue;

            var info = new PackageInfo
            {
                Name = row[0],
                Id = row[1],
                Version = row.Count > 2 ? row[2] : "",
                Source = row.Count > 3 ? row[3] : ""
            };
            list.Add(info);
            if (list.Count >= max) break;
        }
        return list;
    }

    /// <summary>解析 winget 表格行（兼容框线/纯文本两种输出）</summary>
    private static List<string>? ParseTableLine(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return null;
        // 去掉框线字符（┌─┬┐│├┼┤└┴┘ 及分隔线）
        line = line.Replace("│", " ").Replace("┌", " ").Replace("┐", " ").Replace("└", " ")
                   .Replace("┘", " ").Replace("├", " ").Replace("┤", " ").Replace("┬", " ")
                   .Replace("┴", " ").Replace("┼", " ").Replace("─", " ").Trim();
        if (line.Length == 0) return null;
        // 按 2 个以上空格拆分列
        var parts = Regex.Split(line, @"\s{2,}");
        var cols = parts.Where(p => p.Length > 0).Select(p => p.Trim()).ToList();
        return cols.Count >= 2 ? cols : null;
    }

    /// <summary>
    /// 安装包（实时输出回调）。返回退出码。
    /// </summary>
    public static int Install(string id, Action<string>? outputCallback = null, CancellationToken? cancel = null)
    {
        Logger.Info($"winget 安装: {id}");
        var r = CommandRunner.Run(
            WingetExe,
            $"install -e --id \"{id}\" --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            timeoutMs: 0,          // 安装不设超时，由用户取消
            outputCallback: outputCallback,
            cancelToken: cancel);
        return r.ExitCode;
    }

    /// <summary>已安装软件 ID 集合（用于辅助检测：部分工具不在 PATH 但已通过 winget 安装）</summary>
    public static HashSet<string> ListInstalledIds()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var r = CommandRunner.Run(
            WingetExe,
            "list --accept-source-agreements --disable-interactivity",
            timeoutMs: 120_000);
        if (!r.Succeeded) return set;

        foreach (var line in r.Output.Split('\n'))
        {
            var row = ParseTableLine(line);
            if (row is null || row.Count < 2) continue;
            if (row[0].Equals("名称", StringComparison.OrdinalIgnoreCase)
                || row[1].Equals("Id", StringComparison.OrdinalIgnoreCase)) continue;
            set.Add(row[1]);
        }
        return set;
    }

    /// <summary>
    /// 查询所有可更新的 winget 包，返回 (ID 集合, 可更新项列表)。
    /// winget upgrade 表格列：名称 / Id / 版本 / 可用版本 / 源。
    /// </summary>
    public static (HashSet<string> Ids, List<PackageInfo> Items) GetUpgradable()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<PackageInfo>();
        var r = CommandRunner.Run(
            WingetExe,
            "upgrade --accept-source-agreements --disable-interactivity",
            timeoutMs: 120_000);
        if (!r.Succeeded) return (ids, items);

        foreach (var line in r.Output.Split('\n'))
        {
            var row = ParseTableLine(line);
            if (row is null || row.Count < 3) continue;
            if (row[0].Equals("名称", StringComparison.OrdinalIgnoreCase)
                || row[0].Equals("Name", StringComparison.OrdinalIgnoreCase)
                || row[1].Equals("Id", StringComparison.OrdinalIgnoreCase)) continue;
            var info = new PackageInfo
            {
                Name = row[0],
                Id = row[1],
                Version = row.Count > 2 ? row[2] : "",
                Source = row.Count > 4 ? row[4] : ""
            };
            ids.Add(row[1]);
            items.Add(info);
        }
        return (ids, items);
    }

    /// <summary>
    /// 升级单个 winget 包（实时输出回调）。返回退出码。
    /// </summary>
    public static int Upgrade(string id, Action<string>? outputCallback = null, CancellationToken? cancel = null)
    {
        Logger.Info($"winget 升级: {id}");
        var r = CommandRunner.Run(
            WingetExe,
            $"upgrade -e --id \"{id}\" --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            timeoutMs: 0,
            outputCallback: outputCallback,
            cancelToken: cancel);
        return r.ExitCode;
    }

    /// <summary>
    /// 一键更新所有可升级的 winget 包（实时输出回调）。返回退出码。
    /// </summary>
    public static int UpgradeAll(Action<string>? outputCallback = null, CancellationToken? cancel = null)
    {
        Logger.Info("winget 一键更新: upgrade --all");
        var r = CommandRunner.Run(
            WingetExe,
            "upgrade --all --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            timeoutMs: 0,
            outputCallback: outputCallback,
            cancelToken: cancel);
        return r.ExitCode;
    }

    /// <summary>
    /// 卸载单个 winget 包（实时输出回调）。返回退出码。
    /// </summary>
    public static int Uninstall(string id, Action<string>? outputCallback = null, CancellationToken? cancel = null)
    {
        Logger.Info($"winget 卸载: {id}");
        var r = CommandRunner.Run(
            WingetExe,
            $"uninstall -e --id \"{id}\" --silent --accept-source-agreements --disable-interactivity",
            timeoutMs: 0,
            outputCallback: outputCallback,
            cancelToken: cancel);
        return r.ExitCode;
    }
}
