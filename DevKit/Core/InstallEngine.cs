using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using DevKit.Models;

namespace DevKit.Core;

/// <summary>安装进度信息（回调给 UI）</summary>
public class InstallProgress
{
    public required string ToolName { get; init; }
    public string Stage { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool IsIndeterminate { get; set; } = true;
}

/// <summary>
/// 安装引擎：支持多方式回退（winget -> scoop -> official），
/// 前置依赖自动递归安装（如 scoop 方式需要先装 scoop）。
/// 安全原则：优先官方源；包 ID 安装前验证；不执行来源不明命令。
/// </summary>
public class InstallEngine
{
    private readonly Action<InstallProgress> _onProgress;
    private readonly IReadOnlyList<ToolInfo> _allTools;
    private readonly Dictionary<string, ToolDetection> _detections;

    public InstallEngine(Action<InstallProgress> onProgress, IReadOnlyList<ToolInfo> allTools, Dictionary<string, ToolDetection> detections)
    {
        _onProgress = onProgress;
        _allTools = allTools;
        _detections = detections;
    }

    private void Report(string tool, string stage, string detail = "")
        => _onProgress(new InstallProgress { ToolName = tool, Stage = stage, Detail = detail });

    /// <summary>执行安装计划。每项安装后立即复检。</summary>
    public void Execute(List<InstallPlanItem> plan, CancellationToken cancel)
    {
        foreach (var item in plan)
        {
            cancel.ThrowIfCancellationRequested();
            var det = _detections[item.Tool.Name];
            det.InstallResult = null;

            Report(item.Tool.Name, "开始", $"{(item.IsUserSelected ? "用户选择" : item.Reason)}");

            try
            {
                var (ok, note) = InstallOne(item.Tool, cancel);
                cancel.ThrowIfCancellationRequested();
                RedetectOne(det);

                if (ok)
                {
                    det.InstallResult = det.IsInstalled
                        ? $"安装成功（{det.VersionText}）"
                        : $"安装命令已执行，但复检未检测到：{note}";
                    Report(item.Tool.Name, "完成", det.InstallResult);
                }
                else
                {
                    det.InstallResult = $"安装失败：{note}";
                    Report(item.Tool.Name, "失败", det.InstallResult);
                }
            }
            catch (OperationCanceledException)
            {
                det.InstallResult = "已取消";
                Report(item.Tool.Name, "已取消", "");
                throw;
            }
            catch (Exception ex)
            {
                det.InstallResult = $"安装异常：{ex.Message}";
                Logger.Error($"安装 {item.Tool.Name} 异常: {ex}");
                Report(item.Tool.Name, "失败", det.InstallResult);
            }
        }
    }

    /// <summary>安装单个软件，遍历所有可用方式，支持前置依赖递归安装。</summary>
    private (bool Ok, string Note) InstallOne(ToolInfo tool, CancellationToken cancel)
    {
        var methods = tool.Install?.EffectiveMethods;
        if (methods == null || methods.Count == 0)
            return (false, "缺少 install 配置");

        var failures = new List<string>();
        foreach (var method in methods)
        {
            cancel.ThrowIfCancellationRequested();

            // 检查并安装前置依赖
            if (!string.IsNullOrEmpty(method.Requires))
            {
                var depOk = EnsureRequirement(method.Requires, cancel);
                if (!depOk)
                {
                    failures.Add($"{method.Method}: 前置依赖 {method.Requires} 安装失败");
                    Report(tool.Name, "跳过方式", $"{method.Method} 需要 {method.Requires}，但安装失败");
                    continue;
                }
            }

            var (ok, note) = method.MethodEnum switch
            {
                InstallMethod.Winget => InstallViaWinget(tool, method, cancel),
                InstallMethod.Scoop => InstallViaScoop(tool, method, cancel),
                InstallMethod.Bundled => InstallBundled(tool),
                InstallMethod.Official => InstallOfficial(tool, method, cancel),
                _ => (false, $"未知方式 {method.Method}")
            };

            if (ok) return (ok, note);
            failures.Add($"{method.Method}: {note}");
            Report(tool.Name, "方式失败", $"{method.Method} 失败：{note}，尝试下一种方式");
        }

        return (false, string.Join("；", failures));
    }

    /// <summary>确保前置依赖已安装，未安装则递归安装。</summary>
    private bool EnsureRequirement(string depName, CancellationToken cancel)
    {
        if (_detections.TryGetValue(depName, out var det) && det.IsInstalled)
            return true;

        var depTool = _allTools.FirstOrDefault(t => t.Name.Equals(depName, StringComparison.OrdinalIgnoreCase));
        if (depTool == null)
        {
            Logger.Error($"前置依赖 {depName} 不在工具列表中");
            return false;
        }

        Report(depName, "安装前置", $"安装 {depName} 以满足依赖");
        var (ok, note) = InstallOne(depTool, cancel);
        if (ok && _detections.TryGetValue(depName, out var d))
        {
            RedetectOne(d);
            return d.IsInstalled;
        }
        Logger.Error($"前置依赖 {depName} 安装失败: {note}");
        return false;
    }

    private (bool Ok, string Note) InstallViaWinget(ToolInfo tool, InstallMethodConfig method, CancellationToken cancel)
    {
        if (!WingetHelper.IsAvailable())
            return (false, "winget 不可用");

        Report(tool.Name, "检查包", $"验证包 ID: {method.Id}");
        var id = method.Id;
        if (string.IsNullOrEmpty(id) || !WingetHelper.PackageExists(id))
        {
            if (!string.IsNullOrEmpty(method.FallbackId) && WingetHelper.PackageExists(method.FallbackId))
            {
                id = method.FallbackId;
            }
            else
            {
                Report(tool.Name, "未找到包", $"包 ID {method.Id} 不存在，搜索候选...");
                var candidates = WingetHelper.Search(tool.Name, max: 8);
                if (candidates.Count == 0)
                    return (false, $"winget 中未找到 {tool.Name}");
                id = candidates[0].Id;
                Logger.Info($"{tool.Name} 使用搜索到的包 ID: {id}");
            }
        }

        Report(tool.Name, "安装中", $"winget install {id}");
        var exitCode = WingetHelper.Install(id, detail =>
        {
            var clean = Regex.Replace(detail, @"\x1B\[[0-9;]*[A-Za-z]", "").Trim();
            if (clean.Length > 0) Report(tool.Name, "安装中", clean);
        }, cancel);

        return exitCode == 0
            ? (true, $"winget 安装完成（{id}）")
            : (false, $"winget 退出码 {exitCode}（{id}）");
    }

    private (bool Ok, string Note) InstallViaScoop(ToolInfo tool, InstallMethodConfig method, CancellationToken cancel)
    {
        if (string.IsNullOrEmpty(method.Id))
            return (false, "scoop 方式缺少包名");

        // scoop 可能不在 PATH（刚安装），用完整路径
        var scoopPath = FindScoop();
        if (scoopPath == null)
            return (false, "scoop 未找到");

        Report(tool.Name, "安装中", $"scoop install {method.Id}");
        var result = CommandRunner.Run(scoopPath, $"install {method.Id}", timeoutMs: 0,
            outputCallback: detail =>
            {
                var clean = detail.Trim();
                if (clean.Length > 0) Report(tool.Name, "安装中", clean);
            }, cancelToken: cancel);

        if (result.Canceled) return (false, "已取消");
        return result.Succeeded
            ? (true, $"scoop 安装完成（{method.Id}）")
            : (false, $"scoop 退出码 {result.ExitCode}");
    }

    private static string? FindScoop()
    {
        // 常见 scoop 路径
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims", "scoop.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps", "scoop", "current", "bin", "scoop.cmd"),
        };
        foreach (var p in candidates)
            if (File.Exists(p)) return p;
        // PATH 中找
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';'))
        {
            var full = Path.Combine(dir.Trim(), "scoop.cmd");
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private (bool Ok, string Note) InstallBundled(ToolInfo tool)
    {
        Report(tool.Name, "随宿主安装", $"{tool.Name} 由宿主（{string.Join("/", tool.Dependencies)}）提供");
        Logger.Info($"{tool.Name} 为随宿主安装，安装依赖后自动获得");
        return (true, "随宿主自动获得，请确认依赖已安装");
    }

    private (bool Ok, string Note) InstallOfficial(ToolInfo tool, InstallMethodConfig method, CancellationToken cancel)
    {
        var cmd = method.OfficialCommand;
        if (string.IsNullOrEmpty(cmd))
            return (false, "official 方式缺少官方命令");

        Report(tool.Name, "执行官方命令", cmd);
        Logger.Info($"官方安装命令: {cmd}");

        var (exe, args) = SplitCommand(cmd);
        var result = CommandRunner.Run(exe, args, timeoutMs: 0,
            outputCallback: detail =>
            {
                var clean = detail.Trim();
                if (clean.Length > 0) Report(tool.Name, "执行中", clean);
            }, cancelToken: cancel);

        if (result.Canceled) return (false, "已取消");
        return result.Succeeded
            ? (true, "官方命令执行完成")
            : (false, $"命令退出码 {result.ExitCode}");
    }

    private static (string Exe, string Args) SplitCommand(string cmd)
    {
        cmd = cmd.Trim();
        if (cmd.StartsWith('"'))
        {
            var end = cmd.IndexOf('"', 1);
            if (end > 0)
                return (cmd[1..end], cmd[(end + 1)..].Trim());
        }
        var sp = cmd.IndexOf(' ');
        return sp > 0 ? (cmd[..sp], cmd[(sp + 1)..].Trim()) : (cmd, "");
    }

    private void RedetectOne(ToolDetection det)
    {
        var engine = new DetectionEngine();
        var fresh = engine.DetectAll(new[] { det.Tool }, useWingetList: false).First();
        det.Status = fresh.Status;
        det.Version = fresh.Version;
        det.InstallPath = fresh.InstallPath;
        det.Message = fresh.Message;
    }
}
