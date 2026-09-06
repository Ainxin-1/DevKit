using System.IO;
using System.Text.RegularExpressions;

namespace DevKit.Core;

/// <summary>下载进度快照（已下载字节数 + 速度，带格式化文本）</summary>
public class DownloadSnapshot
{
    public long BytesReceived { get; set; }
    public double SpeedBps { get; set; }

    public string SizeText => BytesReceived switch
    {
        < 1024 => $"{BytesReceived} B",
        < 1024 * 1024 => $"{BytesReceived / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{BytesReceived / (1024.0 * 1024):F1} MB",
        _ => $"{BytesReceived / (1024.0 * 1024 * 1024):F2} GB"
    };

    public string SpeedText => SpeedBps switch
    {
        < 1024 => $"{SpeedBps:F0} B/s",
        < 1024 * 1024 => $"{SpeedBps / 1024.0:F1} KB/s",
        _ => $"{SpeedBps / (1024.0 * 1024):F1} MB/s"
    };
}

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

    /// <summary>候选评分结果</summary>
    public class ScoredCandidate
    {
        public PackageInfo Package { get; init; } = new();
        public int Score { get; init; }
        public string Reason { get; init; } = "";
    }

    /// <summary>
    /// 从搜索候选中选择最佳匹配。返回 (选中的包, 是否确定唯一)。
    /// 评分规则：ID 精确匹配 > 名称精确匹配 > 名称高度相似 > Publisher 合理匹配。
    /// 无法确定唯一结果时返回 null，调用方应要求人工选择。
    /// </summary>
    public static (PackageInfo? Best, bool Confident) SelectBestMatch(string toolName, List<PackageInfo> candidates)
    {
        if (candidates.Count == 0) return (null, false);
        if (candidates.Count == 1) return (candidates[0], true);

        var scored = candidates.Select(c =>
        {
            int score = 0;
            var reasons = new List<string>();

            // ID 精确匹配（如 Git.Git）
            if (c.Id.Equals(toolName, StringComparison.OrdinalIgnoreCase))
            { score += 100; reasons.Add("ID精确匹配"); }

            // ID 包含工具名（如 OpenJS.NodeJS 包含 Node）
            if (c.Id.Contains(toolName, StringComparison.OrdinalIgnoreCase))
            { score += 40; reasons.Add("ID包含名称"); }

            // 名称精确匹配
            if (c.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase))
            { score += 80; reasons.Add("名称精确匹配"); }

            // 名称包含工具名
            if (c.Name.Contains(toolName, StringComparison.OrdinalIgnoreCase))
            { score += 30; reasons.Add("名称包含"); }

            // 名称高度相似（去掉空格/标点后比较）
            var normName = Normalize(c.Name);
            var normTool = Normalize(toolName);
            if (normName == normTool) { score += 60; reasons.Add("名称归一化匹配"); }
            else if (normName.Contains(normTool) || normTool.Contains(normName))
            { score += 20; reasons.Add("名称归一化包含"); }

            // 官方 Publisher 特征
            if (IsOfficialPublisher(c.Id, toolName))
            { score += 25; reasons.Add("官方发布者"); }

            // winget 源优先于 msstore
            if (c.Source.Equals("winget", StringComparison.OrdinalIgnoreCase))
            { score += 10; reasons.Add("winget源"); }

            return new ScoredCandidate { Package = c, Score = score, Reason = string.Join(",", reasons) };
        }).OrderByDescending(s => s.Score).ToList();

        var top = scored[0];
        Logger.Info($"候选评分: {top.Package.Name}({top.Package.Id}) score={top.Score} [{top.Reason}]");

        // 最高分 >= 60 且领先第二名 >= 20，认为确定
        if (top.Score >= 60 && (scored.Count < 2 || top.Score - scored[1].Score >= 20))
        {
            return (top.Package, true);
        }

        // 无法确定，返回 null
        Logger.Warn($"无法确定 {toolName} 的唯一 winget 包，候选: {string.Join("; ", scored.Take(3).Select(s => $"{s.Package.Name}({s.Score})"))}");
        return (null, false);
    }

    private static string Normalize(string s)
        => new string(s.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToLowerInvariant();

    private static bool IsOfficialPublisher(string id, string toolName)
    {
        // 常见官方发布者前缀
        var officialPrefixes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Git"] = new[] { "Git." },
            ["Python"] = new[] { "Python." },
            ["Node.js"] = new[] { "OpenJS." },
            ["JDK"] = new[] { "EclipseAdoptium.", "Oracle.", "Microsoft." },
            ["Go"] = new[] { "GoLang." },
            ["Rust"] = new[] { "Rustlang." },
            [".NET SDK"] = new[] { "Microsoft.DotNet" },
            ["Docker Desktop"] = new[] { "Docker." },
            ["VS Code"] = new[] { "Microsoft.VisualStudioCode" },
            ["Flutter"] = new[] { "Google." },
            ["PHP"] = new[] { "PHP." },
            ["Ruby"] = new[] { "RubyInstallerTeam." },
            ["Swift"] = new[] { "Swift." },
            ["CUDA"] = new[] { "Nvidia." },
            ["pnpm"] = new[] { "pnpm." },
            ["Yarn"] = new[] { "Yarn." },
            ["Bun"] = new[] { "Oven-sh." },
            ["uv"] = new[] { "astral-sh." },
            ["Conda"] = new[] { "Anaconda." },
            ["Conan"] = new[] { "JFrog." },
            ["Chocolatey"] = new[] { "Chocolatey." },
            ["Zed"] = new[] { "ZedIndustries." },
        };
        if (officialPrefixes.TryGetValue(toolName, out var prefixes))
        {
            return prefixes.Any(p => id.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }
        return false;
    }

    /// <summary>解析 winget 表格行（兼容框线/纯文本两种输出）。解析失败返回 null，不猜列。</summary>
    private static List<string>? ParseTableLine(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return null;
        // 去掉框线字符（┌─┬┐│├┼┤└┴┘ 及分隔线）
        line = line.Replace("│", " ").Replace("┌", " ").Replace("┐", " ").Replace("└", " ")
                   .Replace("┘", " ").Replace("├", " ").Replace("┤", " ").Replace("┬", " ")
                   .Replace("┴", " ").Replace("┼", " ").Replace("─", " ").Trim();
        if (line.Length == 0) return null;
        // 全是分隔线（---）跳过
        if (line.All(c => c == '-' || c == ' ')) return null;

        // 按 2 个以上空格拆分列
        var parts = Regex.Split(line, @"\s{2,}");
        var cols = parts.Where(p => p.Length > 0).Select(p => p.Trim()).ToList();

        // 列数验证：winget 表格至少 2 列（Name, Id），通常 3-4 列
        if (cols.Count < 2) return null;

        // 安全检查：Id 列应包含点号（如 Git.Git）或为已知格式
        // 但不强制，因为有些包 ID 可能没有点号
        // 只确保第一列不是空的、不是纯数字
        if (string.IsNullOrWhiteSpace(cols[0]) || cols[0].All(char.IsDigit)) return null;

        return cols;
    }

    // ---------- 下载进度监控 ----------
    // winget 在 stdout 被重定向时自动禁用动态进度条输出（非 TTY 检测），
    // 所以 ParseProgress 从文本中匹配不到 XX%。这里改为监控 winget 临时下载
    // 目录中新增文件的大小变化来计算已下载字节数和速度。

    /// <summary>winget 可能的临时下载目录（按版本不同路径有差异）</summary>
    private static string[] GetWinGetTempDirs()
    {
        var temp = Path.GetTempPath();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new[]
        {
            Path.Combine(temp, "WinGet", "Packages"),
            Path.Combine(temp, "WinGet"),
            Path.Combine(localAppData, "Temp", "WinGet", "Packages"),
            Path.Combine(localAppData, "Temp", "WinGet"),
        };
    }

    /// <summary>扫描下载目录中新增文件的总大小（排除启动前已存在的文件）</summary>
    private static long ScanDownloadedBytes(string[] dirs, HashSet<string> snapshot)
    {
        long total = 0;
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            string[] files;
            try { files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories); }
            catch { continue; }
            foreach (var f in files)
            {
                if (snapshot.Contains(f)) continue;
                try { total += new FileInfo(f).Length; } catch { }
            }
        }
        return total;
    }

    /// <summary>运行 winget 命令并附带下载进度监控</summary>
    private static int RunWingetWithMonitor(string args, Action<string>? outputCallback,
        Action<DownloadSnapshot>? downloadCallback, CancellationToken? cancel)
    {
        var cts = cancel ?? CancellationToken.None;

        // 启动前记录下载目录快照（用于区分哪些是本次下载的新文件）
        var dirs = GetWinGetTempDirs();
        var snapshot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirs)
            if (Directory.Exists(dir))
                try { foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) snapshot.Add(f); } catch { }

        // 启动下载进度监控任务
        using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cts);
        long lastBytes = 0;
        DateTime lastTime = DateTime.MinValue;
        var monitor = Task.Run(async () =>
        {
            while (!monitorCts.IsCancellationRequested)
            {
                var bytes = ScanDownloadedBytes(dirs, snapshot);
                var now = DateTime.Now;
                double speed = 0;
                if (lastTime != DateTime.MinValue)
                {
                    var elapsed = (now - lastTime).TotalSeconds;
                    if (elapsed > 0.01) speed = (bytes - lastBytes) / elapsed;
                }
                lastTime = now;
                lastBytes = bytes;
                downloadCallback?.Invoke(new DownloadSnapshot
                {
                    BytesReceived = bytes,
                    SpeedBps = Math.Max(0, speed)
                });
                try { await Task.Delay(800, monitorCts.Token); } catch { break; }
            }
        }, monitorCts.Token);

        try
        {
            var r = CommandRunner.Run(WingetExe, args, timeoutMs: 0,
                outputCallback: outputCallback, cancelToken: cancel);
            return r.ExitCode;
        }
        finally
        {
            monitorCts.Cancel();
            try { monitor.Wait(1000); } catch { }
        }
    }

    /// <summary>
    /// 安装包（实时输出回调 + 下载进度监控）。返回退出码。
    /// </summary>
    public static int Install(string id, Action<string>? outputCallback = null,
        Action<DownloadSnapshot>? downloadCallback = null, CancellationToken? cancel = null)
    {
        Logger.Info($"winget 安装: {id}");
        return RunWingetWithMonitor(
            $"install -e --id \"{id}\" --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            outputCallback, downloadCallback, cancel);
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
    /// 升级单个 winget 包（实时输出回调 + 下载进度监控）。返回退出码。
    /// </summary>
    public static int Upgrade(string id, Action<string>? outputCallback = null,
        Action<DownloadSnapshot>? downloadCallback = null, CancellationToken? cancel = null)
    {
        Logger.Info($"winget 升级: {id}");
        return RunWingetWithMonitor(
            $"upgrade -e --id \"{id}\" --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            outputCallback, downloadCallback, cancel);
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
