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
    private readonly HashSet<string> _installing = new(StringComparer.OrdinalIgnoreCase); // 循环依赖保护

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
            det.InstallStatus = InstallResultStatus.None;

            Report(item.Tool.Name, "开始", $"{(item.IsUserSelected ? "用户选择" : item.Reason)}");

            try
            {
                var (ok, note, status) = InstallOne(item.Tool, cancel);
                cancel.ThrowIfCancellationRequested();
                RedetectOne(det);

                if (ok)
                {
                    if (det.IsInstalled)
                    {
                        det.InstallStatus = InstallResultStatus.Success;
                        det.InstallResult = $"安装成功（{det.VersionText}）";
                    }
                    else
                    {
                        det.InstallStatus = InstallResultStatus.DetectionFailed;
                        det.InstallResult = $"安装命令执行成功，但复检未检测到：{note}。请检查 PATH 或安装目录。";
                    }
                    Report(item.Tool.Name, "完成", det.InstallResult);
                }
                else
                {
                    det.InstallStatus = status;
                    det.InstallResult = status switch
                    {
                        InstallResultStatus.DependencyFailed => $"依赖安装失败：{note}",
                        InstallResultStatus.RequiresUserSelection => $"需要用户选择：{note}",
                        InstallResultStatus.Cancelled => "已取消",
                        _ => $"安装失败：{note}"
                    };
                    Report(item.Tool.Name, "失败", det.InstallResult);
                }
            }
            catch (OperationCanceledException)
            {
                det.InstallStatus = InstallResultStatus.Cancelled;
                det.InstallResult = "已取消";
                Report(item.Tool.Name, "已取消", "");
                throw;
            }
            catch (Exception ex)
            {
                det.InstallStatus = InstallResultStatus.Failed;
                det.InstallResult = $"安装异常：{ex.Message}";
                Logger.Error($"安装 {item.Tool.Name} 异常: {ex}");
                Report(item.Tool.Name, "失败", det.InstallResult);
            }
        }
    }

    /// <summary>安装单个软件，遍历所有可用方式，支持前置依赖递归安装。</summary>
    private (bool Ok, string Note, InstallResultStatus Status) InstallOne(ToolInfo tool, CancellationToken cancel)
    {
        var methods = tool.Install?.EffectiveMethods;
        if (methods == null || methods.Count == 0)
            return (false, "缺少 install 配置", InstallResultStatus.Failed);

        var failures = new List<string>();
        foreach (var method in methods)
        {
            cancel.ThrowIfCancellationRequested();

            // 检查并安装前置依赖
            if (!string.IsNullOrEmpty(method.Requires))
            {
                var (depOk, depNote) = EnsureRequirement(method.Requires, cancel);
                if (!depOk)
                {
                    failures.Add($"{method.Method}: 前置依赖 {method.Requires} 安装失败");
                    Report(tool.Name, "跳过方式", $"{method.Method} 需要 {method.Requires}，但安装失败");
                    return (false, $"{method.Requires}: {depNote}", InstallResultStatus.DependencyFailed);
                }
            }

            var (ok, note, status) = method.MethodEnum switch
            {
                InstallMethod.Winget => InstallViaWinget(tool, method, cancel),
                InstallMethod.Scoop => InstallViaScoop(tool, method, cancel),
                InstallMethod.Bundled => InstallBundled(tool),
                InstallMethod.Official => InstallOfficial(tool, method, cancel),
                _ => (false, $"未知方式 {method.Method}", InstallResultStatus.Failed)
            };

            if (ok) return (ok, note, status);
            if (status == InstallResultStatus.RequiresUserSelection)
                return (false, note, status); // 候选不唯一，不继续回退
            failures.Add($"{method.Method}: {note}");
            Report(tool.Name, "方式失败", $"{method.Method} 失败：{note}，尝试下一种方式");
        }

        return (false, string.Join("；", failures), InstallResultStatus.Failed);
    }

    /// <summary>确保前置依赖已安装，未安装则递归安装。带循环依赖保护。</summary>
    private (bool Ok, string Note) EnsureRequirement(string depName, CancellationToken cancel)
    {
        if (_detections.TryGetValue(depName, out var det) && det.IsInstalled)
            return (true, "");

        // 循环依赖检测
        if (!_installing.Add(depName))
        {
            var cycle = string.Join(" -> ", _installing) + " -> " + depName;
            Logger.Error($"检测到安装依赖循环: {cycle}");
            return (false, $"检测到安装依赖循环: {cycle}");
        }

        try
        {
            var depTool = _allTools.FirstOrDefault(t => t.Name.Equals(depName, StringComparison.OrdinalIgnoreCase));
            if (depTool == null)
            {
                Logger.Error($"前置依赖 {depName} 不在工具列表中");
                return (false, $"{depName} 不在工具列表中");
            }

            Report(depName, "安装前置", $"安装 {depName} 以满足依赖");
            var (ok, note, _) = InstallOne(depTool, cancel);
            if (ok && _detections.TryGetValue(depName, out var d))
            {
                RedetectOne(d);
                if (d.IsInstalled) return (true, "");
                return (false, $"{depName} 安装命令成功但复检未检测到");
            }
            Logger.Error($"前置依赖 {depName} 安装失败: {note}");
            return (false, note);
        }
        finally
        {
            _installing.Remove(depName);
        }
    }

    private (bool Ok, string Note, InstallResultStatus Status) InstallViaWinget(ToolInfo tool, InstallMethodConfig method, CancellationToken cancel)
    {
        if (!WingetHelper.IsAvailable())
            return (false, "winget 不可用", InstallResultStatus.Failed);

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
                    return (false, $"winget 中未找到 {tool.Name}", InstallResultStatus.Failed);

                var (best, confident) = WingetHelper.SelectBestMatch(tool.Name, candidates);
                if (best == null || !confident)
                {
                    var candidateList = string.Join("; ", candidates.Take(5).Select(c => $"{c.Name}({c.Id})"));
                    Logger.Warn($"{tool.Name} winget 候选不唯一，无法安全自动安装: {candidateList}");
                    return (false, $"winget 候选不唯一，需人工选择: {candidateList}", InstallResultStatus.RequiresUserSelection);
                }
                id = best.Id;
                Logger.Info($"{tool.Name} 使用评分最高的包: {best.Name} ({best.Id}) source={best.Source}");
            }
        }

        Report(tool.Name, "下载中", $"winget 下载 {id}");
        var exitCode = WingetHelper.Install(id,
            outputCallback: detail =>
            {
                var clean = Regex.Replace(detail, @"\x1B\[[0-9;]*[A-Za-z]", "").Trim();
                if (clean.Length > 0) Report(tool.Name, "安装中", clean);
            },
            downloadCallback: snap =>
            {
                if (snap.BytesReceived > 0)
                    Report(tool.Name, "下载中", $"已下载 {snap.SizeText}（{snap.SpeedText}）");
            },
            cancel: cancel);

        return exitCode == 0
            ? (true, $"winget 安装完成（{id}）", InstallResultStatus.Success)
            : (false, $"winget 退出码 {exitCode}（{id}）", InstallResultStatus.Failed);
    }

    private (bool Ok, string Note, InstallResultStatus Status) InstallViaScoop(ToolInfo tool, InstallMethodConfig method, CancellationToken cancel)
    {
        if (string.IsNullOrEmpty(method.Id))
            return (false, "scoop 方式缺少包名", InstallResultStatus.Failed);

        // scoop 可能不在 PATH（刚安装），用完整路径
        var scoopPath = FindScoop();
        if (scoopPath == null)
            return (false, "scoop 未找到", InstallResultStatus.Failed);

        Report(tool.Name, "安装中", $"scoop install {method.Id}");
        var result = CommandRunner.Run(scoopPath, $"install {method.Id}", timeoutMs: 0,
            outputCallback: detail =>
            {
                var clean = detail.Trim();
                if (clean.Length > 0) Report(tool.Name, "安装中", clean);
            }, cancelToken: cancel);

        if (result.Canceled) return (false, "已取消", InstallResultStatus.Cancelled);
        return result.Succeeded
            ? (true, $"scoop 安装完成（{method.Id}）", InstallResultStatus.Success)
            : (false, $"scoop 退出码 {result.ExitCode}", InstallResultStatus.Failed);
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

    private (bool Ok, string Note, InstallResultStatus Status) InstallBundled(ToolInfo tool)
    {
        var deps = string.Join("/", tool.Dependencies);
        Report(tool.Name, "随宿主安装", $"{tool.Name} 由宿主（{deps}）提供，安装宿主后自动获得");
        Logger.Info($"{tool.Name} 为随宿主安装（依赖: {deps}），将通过复检确认是否实际可用");
        // 返回 true 表示"命令已执行"，最终结果由复检判断
        // 如果宿主安装成功但 bundled 工具实际不存在，复检会显示"未检测到"
        return (true, $"随宿主（{deps}）自动获得，等待复检确认", InstallResultStatus.Success);
    }

    private (bool Ok, string Note, InstallResultStatus Status) InstallOfficial(ToolInfo tool, InstallMethodConfig method, CancellationToken cancel)
    {
        // 优先使用结构化配置（officialExe + officialArgs），其次旧格式 officialCommand
        string exe, args;
        if (!string.IsNullOrEmpty(method.OfficialExe))
        {
            exe = method.OfficialExe;
            args = method.OfficialArgs ?? "";
        }
        else if (!string.IsNullOrEmpty(method.OfficialCommand))
        {
            // 旧格式：解析但禁止危险命令链
            var validation = ValidateOfficialCommand(method.OfficialCommand);
            if (!validation.Ok) return (false, validation.Note, InstallResultStatus.Failed);
            (exe, args) = SplitCommand(method.OfficialCommand);
        }
        else
        {
            return (false, "official 方式缺少 officialExe 或 officialCommand", InstallResultStatus.Failed);
        }

        // 安全校验：禁止通过 cmd /c、powershell -Command 等形成无限制命令链
        var exeLower = exe.ToLowerInvariant();
        var isScoopInstall = tool.Name.Equals("Scoop", StringComparison.OrdinalIgnoreCase)
                             && args.Contains("get.scoop.sh", StringComparison.OrdinalIgnoreCase);
        if ((exeLower == "cmd" || exeLower == "cmd.exe" || exeLower == "powershell" || exeLower == "powershell.exe" || exeLower == "pwsh" || exeLower == "pwsh.exe")
            && !isScoopInstall)
        {
            if (args.Contains("-Command", StringComparison.OrdinalIgnoreCase) || args.Contains(" -c ", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Error($"official 方式禁止无限制命令链: {exe} {args}");
                return (false, "official 方式禁止通过 cmd/powershell -Command 执行任意命令", InstallResultStatus.Failed);
            }
        }

        Report(tool.Name, "执行官方命令", $"{exe} {args}");
        Logger.Info($"官方安装命令: {exe} {args}");

        var result = CommandRunner.Run(exe, args, timeoutMs: 0,
            outputCallback: detail =>
            {
                var clean = detail.Trim();
                if (clean.Length > 0) Report(tool.Name, "执行中", clean);
            }, cancelToken: cancel);

        if (result.Canceled) return (false, "已取消", InstallResultStatus.Cancelled);
        return result.Succeeded
            ? (true, "官方命令执行完成", InstallResultStatus.Success)
            : (false, $"命令退出码 {result.ExitCode}", InstallResultStatus.Failed);
    }

    /// <summary>校验 official 命令安全性</summary>
    private static (bool Ok, string Note) ValidateOfficialCommand(string cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return (false, "official 命令为空");
        var lower = cmd.ToLowerInvariant();
        // 禁止管道、命令链、重定向
        if (lower.Contains('|') || lower.Contains("&&") || lower.Contains(";") || lower.Contains(">"))
        {
            // 允许 iwr -useb get.scoop.sh | iex 这种官方安装脚本？不，这也是管道
            // 但 Scoop 的官方安装就是管道命令，需要特殊处理
            if (lower.Contains("iwr") && lower.Contains("get.scoop.sh") && lower.Contains("iex"))
            {
                return (true, ""); // Scoop 官方安装脚本，白名单
            }
            Logger.Error($"official 命令包含危险字符: {cmd}");
            return (false, "official 命令禁止包含管道/命令链/重定向");
        }
        return (true, "");
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
        // 安装后刷新当前进程 PATH，使复检能立即找到新工具
        PathManager.RefreshCurrentProcessPath();
        var engine = new DetectionEngine();
        var fresh = engine.DetectAll(new[] { det.Tool }, useWingetList: false).First();
        det.Status = fresh.Status;
        det.Version = fresh.Version;
        det.InstallPath = fresh.InstallPath;
        det.Message = fresh.Message;
    }
}
