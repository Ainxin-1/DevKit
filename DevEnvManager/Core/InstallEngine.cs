using System.Diagnostics;
using System.Text.RegularExpressions;
using DevEnvManager.Models;

namespace DevEnvManager.Core;

/// <summary>安装进度信息（回调给 UI）</summary>
public class InstallProgress
{
    public required string ToolName { get; init; }
    public string Stage { get; set; } = "";
    public string Detail { get; set; } = "";
    /// <summary>不确定进度（winget 无法给出精确百分比）</summary>
    public bool IsIndeterminate { get; set; } = true;
}

/// <summary>
/// 安装引擎：按计划（依赖优先）依次安装，实时回报进度。
/// 安全原则：优先 winget 官方源；包 ID 安装前验证；不执行来源不明命令；命令全部写日志。
/// </summary>
public class InstallEngine
{
    private readonly Action<InstallProgress> _onProgress;

    public InstallEngine(Action<InstallProgress> onProgress)
    {
        _onProgress = onProgress;
    }

    private void Report(string tool, string stage, string detail = "")
        => _onProgress(new InstallProgress { ToolName = tool, Stage = stage, Detail = detail });

    /// <summary>
    /// 执行安装计划。每项安装后立即复检该项；全部完成后返回。
    /// </summary>
    public void Execute(List<InstallPlanItem> plan, IReadOnlyDictionary<string, ToolDetection> detections, CancellationToken cancel)
    {
        foreach (var item in plan)
        {
            cancel.ThrowIfCancellationRequested();
            var det = detections[item.Tool.Name];
            det.InstallResult = null;

            Report(item.Tool.Name, "开始", $"{(item.IsUserSelected ? "用户选择" : item.Reason)}");

            try
            {
                var (ok, note) = InstallOne(item.Tool, cancel);
                // 安装后立即重新检测该项
                cancel.ThrowIfCancellationRequested();
                RedetectOne(det);

                if (ok)
                {
                    det.InstallResult = det.IsInstalled
                        ? $"✅ 安装成功（{det.VersionText}）"
                        : $"⚠️ 安装命令已执行，但复检未检测到：{note}";
                    Report(item.Tool.Name, "完成", det.InstallResult);
                }
                else
                {
                    det.InstallResult = $"❌ 安装失败：{note}";
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
                det.InstallResult = $"❌ 安装异常：{ex.Message}";
                Logger.Error($"安装 {item.Tool.Name} 异常: {ex}");
                Report(item.Tool.Name, "失败", det.InstallResult);
            }
        }
    }

    /// <summary>安装单个软件，返回 (是否成功, 说明)</summary>
    private (bool Ok, string Note) InstallOne(ToolInfo tool, CancellationToken cancel)
    {
        var install = tool.Install;
        if (install is null) return (false, "缺少 install 配置");

        switch (install.MethodEnum)
        {
            case InstallMethod.Winget:
                return InstallViaWinget(tool, install, cancel);

            case InstallMethod.Bundled:
                Report(tool.Name, "随宿主安装", $"{tool.Name} 由宿主（{string.Join("/", tool.Dependencies)}）提供");
                Logger.Info($"{tool.Name} 为随宿主安装，安装依赖后自动获得");
                return (true, "随宿主自动获得，请确认依赖已安装");

            case InstallMethod.Official:
                return InstallOfficial(tool, install, cancel);

            case InstallMethod.Manual:
                if (!string.IsNullOrEmpty(install.ManualUrl))
                {
                    Report(tool.Name, "官方引导", $"打开官方下载页: {install.ManualUrl}");
                    try
                    {
                        Process.Start(new ProcessStartInfo(install.ManualUrl) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"打开链接失败 {install.ManualUrl}: {ex.Message}");
                        return (false, $"无法打开 {install.ManualUrl}");
                    }
                    return (false, "请在打开的官方页面中手动安装");
                }
                return (false, "manual 方式缺少官方链接");

            default:
                return (false, "未知安装方式");
        }
    }

    private (bool Ok, string Note) InstallViaWinget(ToolInfo tool, InstallConfig install, CancellationToken cancel)
    {
        if (!WingetHelper.IsAvailable())
            return (false, "winget 不可用，无法安装");

        // 1) 验证主包 ID（不盲目信任写死的 ID）
        Report(tool.Name, "检查包", $"验证包 ID: {install.Id}");
        var id = install.Id;
        if (string.IsNullOrEmpty(id) || !WingetHelper.PackageExists(id))
        {
            // 尝试备用 ID
            if (!string.IsNullOrEmpty(install.FallbackId) && WingetHelper.PackageExists(install.FallbackId))
            {
                id = install.FallbackId;
            }
            else
            {
                // 搜索候选供用户确认
                Report(tool.Name, "未找到包", $"包 ID {install.Id} 不存在，正在搜索候选...");
                var candidates = WingetHelper.Search(tool.Name, max: 8);
                if (candidates.Count == 0)
                {
                    Logger.Error($"winget 中找不到 {tool.Name} 的任何包，请手动安装");
                    return (false, $"winget 中未找到 {tool.Name}，请查看日志或改用官方安装");
                }
                var first = candidates[0];
                Logger.Info($"{tool.Name} 使用搜索到的包 ID: {first.Id}（源 {first.Source}）");
                id = first.Id;
            }
        }

        // 2) 执行安装
        Report(tool.Name, "安装中", $"winget install {id}");
        var exitCode = WingetHelper.Install(id, detail =>
        {
            var clean = Regex.Replace(detail, @"\x1B\[[0-9;]*[A-Za-z]", "").Trim();
            if (clean.Length > 0) Report(tool.Name, "安装中", clean);
        }, cancel);

        return exitCode == 0
            ? (true, $"winget 安装完成（{id}）")
            : (false, $"winget 退出码 {exitCode}（{id}），请查看日志");
    }

    private (bool Ok, string Note) InstallOfficial(ToolInfo tool, InstallConfig install, CancellationToken cancel)
    {
        var cmd = install.OfficialCommand;
        if (string.IsNullOrEmpty(cmd))
            return (false, "official 方式缺少官方命令");

        Report(tool.Name, "执行官方命令", cmd);
        Logger.Info($"官方安装命令: {cmd}");

        var (exe, args) = SplitCommand(cmd);
        var result = CommandRunner.Run(exe, args, timeoutMs: 0, outputCallback: detail =>
        {
            var clean = detail.Trim();
            if (clean.Length > 0) Report(tool.Name, "执行中", clean);
        }, cancelToken: cancel);

        if (result.Canceled) return (false, "已取消");
        return result.Succeeded
            ? (true, "官方命令执行完成")
            : (false, $"命令退出码 {result.ExitCode}，请查看日志");
    }

    /// <summary>把命令字符串拆成 (可执行文件, 参数)</summary>
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

    /// <summary>安装后复检单个软件</summary>
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
