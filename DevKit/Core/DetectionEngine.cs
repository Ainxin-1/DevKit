using System.IO;
using System.Text.RegularExpressions;
using DevKit.Models;

namespace DevKit.Core;

/// <summary>
/// 检测引擎：在 PATH / 环境变量 / 常见目录中检测软件，获取版本与安装路径。
/// </summary>
public class DetectionEngine
{
    /// <summary>辅助：winget 已安装包 ID 集合（命令不在 PATH 时用于确认已装）</summary>
    public HashSet<string> WingetInstalledIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>检测单个软件（供分批渐进式检测调用）</summary>
    public ToolDetection DetectSingle(ToolInfo tool)
    {
        var detection = new ToolDetection { Tool = tool, Status = DetectionStatus.Detecting };
        try { DetectOne(detection); }
        catch (Exception ex)
        {
            detection.Status = DetectionStatus.DetectFailed;
            detection.Message = ex.Message;
            Logger.Warn($"检测 {tool.Name} 异常: {ex.Message}");
        }
        return detection;
    }

    /// <summary>
    /// 执行全量检测（耗时操作，应在后台线程调用）。
    /// 并行执行各软件的版本命令，大幅缩短检测时间。
    /// </summary>
    public List<ToolDetection> DetectAll(IEnumerable<ToolInfo> tools, bool useWingetList = true)
    {
        if (useWingetList)
        {
            try { WingetInstalledIds = WingetHelper.ListInstalledIds(); }
            catch { WingetInstalledIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        }

        var toolList = tools.ToList();
        var results = new ToolDetection[toolList.Count];

        // 并行检测：每个软件独立，版本命令可同时执行
        Parallel.For(0, toolList.Count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var detection = new ToolDetection { Tool = toolList[i], Status = DetectionStatus.Detecting };
            try
            {
                DetectOne(detection);
            }
            catch (Exception ex)
            {
                detection.Status = DetectionStatus.DetectFailed;
                detection.Message = ex.Message;
                Logger.Warn($"检测 {toolList[i].Name} 异常: {ex.Message}");
            }
            results[i] = detection;
        });

        return results.ToList();
    }

    private void DetectOne(ToolDetection d)
    {
        var tool = d.Tool;
        var cfg = tool.Detect;

        if (cfg is null)
        {
            d.Status = DetectionStatus.DetectFailed;
            d.Message = "缺少 detect 配置";
            return;
        }

        // 1) 环境变量检测（如 JAVA_HOME / ANDROID_HOME）
        if (!string.IsNullOrEmpty(cfg.EnvVar))
        {
            var env = Environment.GetEnvironmentVariable(cfg.EnvVar);
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            {
                d.InstallPath = env;
                if (tool.Name.Equals("Android SDK", StringComparison.OrdinalIgnoreCase))
                    DetectAndroidComponents(d, env);
            }
        }

        // 2) 命令检测（PATH）
        var exePath = FindExecutable(cfg.Command);
        if (exePath is not null)
        {
            if (d.InstallPath is null) d.InstallPath = exePath;
            // 规避 Windows 商店占位别名：执行版本命令验证
            if (IsStoreAlias(exePath))
            {
                var probe = CommandRunner.Run(exePath, cfg.VersionArgs, timeoutMs: 5_000);
                if (probe.Output.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    || probe.Output.Contains("未安装", StringComparison.OrdinalIgnoreCase)
                    || probe.Output.Contains("没有安装", StringComparison.OrdinalIgnoreCase))
                {
                    d.Status = DetectionStatus.NotInstalled;
                    return;
                }
            }
            var (version, raw) = GetVersion(exePath, cfg);
            d.Version = version;
            d.Status = DetermineStatus(version, cfg.MinVersion, out var msg);
            d.Message = msg;
            return;
        }

        // 3) 常见安装目录（PATH 未刷新但已安装）
        foreach (var hint in cfg.PathHints)
        {
            var expanded = Environment.ExpandEnvironmentVariables(hint);
            if (Directory.Exists(expanded))
            {
                d.InstallPath = expanded;
                d.Status = DetectionStatus.Installed;
                d.Version = null;
                d.Message = "已检测到安装目录（可能未加入 PATH）";
                if (tool.Name.Equals("Android SDK", StringComparison.OrdinalIgnoreCase))
                    DetectAndroidComponents(d, expanded);
                return;
            }
        }

        // 4) winget 列表兜底（已通过 winget 安装但命令不在 PATH）
        var wid = tool.Install?.Id;
        if (!string.IsNullOrEmpty(wid) && WingetInstalledIds.Contains(wid))
        {
            d.Status = DetectionStatus.Installed;
            d.Version = null;
            d.Message = $"已通过 winget 安装（{wid}），但命令未在 PATH 中";
            return;
        }

        d.Status = DetectionStatus.NotInstalled;
    }

    /// <summary>
    /// Android SDK 组件检测：platforms / build-tools / platform-tools / cmdline-tools / emulator / adb。
    /// 区分"SDK 已安装"和"组件完整"，关键组件缺失时状态改为 VersionUnknown。
    /// </summary>
    private void DetectAndroidComponents(ToolDetection d, string sdkDir)
    {
        var parts = new List<string>();
        var missing = new List<string>();
        void Add(string label, string sub, bool critical)
        {
            var p = Path.Combine(sdkDir, sub);
            var exists = Directory.Exists(p);
            parts.Add($"{label}:{(exists ? "✓" : "✗")}");
            if (!exists && critical) missing.Add(label);
        }
        Add("Platform-Tools", "platform-tools", critical: true);
        Add("Build-Tools", "build-tools", critical: true);
        Add("SDK Platform", "platforms", critical: true);
        Add("Cmdline-Tools", "cmdline-tools", critical: false);
        Add("Emulator", "emulator", critical: false);
        var adb = Path.Combine(sdkDir, "platform-tools", "adb.exe");
        var adbExists = File.Exists(adb);
        parts.Add($"ADB:{(adbExists ? "✓" : "✗")}");
        if (!adbExists) missing.Add("ADB");

        d.Message = "组件: " + string.Join(" ", parts);
        if (missing.Count > 0)
        {
            d.Status = DetectionStatus.VersionUnknown;
            d.Message += $"（缺失: {string.Join(", ", missing)}）";
            Logger.Warn($"Android SDK 目录存在但关键组件缺失: {string.Join(", ", missing)}");
        }
    }

    /// <summary>根据版本与最低版本判定状态</summary>
    private DetectionStatus DetermineStatus(string? version, string? minVersion, out string? message)
    {
        message = null;
        if (string.IsNullOrEmpty(version))
        {
            // 命令存在但版本解析失败，标记为 VersionUnknown 而不是 Installed
            message = "已找到程序，但无法确认版本";
            return DetectionStatus.VersionUnknown;
        }
        if (string.IsNullOrEmpty(minVersion))
        {
            return DetectionStatus.Installed;
        }
        if (TryParseVersion(version, out var cur) && TryParseVersion(minVersion, out var min))
        {
            if (cur < min)
            {
                message = $"当前 {version}，推荐 ≥ {minVersion}";
                return DetectionStatus.VersionTooLow;
            }
        }
        return DetectionStatus.Installed;
    }

    /// <summary>执行版本命令并提取版本号</summary>
    private (string? Version, string Raw) GetVersion(string exePath, DetectConfig cfg)
    {
        var args = string.IsNullOrEmpty(cfg.VersionArgs) ? "" : cfg.VersionArgs;
        var r = CommandRunner.Run(exePath, args, timeoutMs: 5_000);
        if (!r.Succeeded && string.IsNullOrEmpty(r.Output)) return (null, r.Output);

        var match = Regex.Match(r.Output, cfg.VersionRegex);
        return match.Success ? (match.Groups[1].Value, r.Output) : (null, r.Output);
    }

    /// <summary>在 PATH 中查找可执行文件</summary>
    public static string? FindExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM";
        var extensions = pathext.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir.Trim('"'), command);
            foreach (var ext in extensions)
            {
                var candidate = full + ext;
                if (File.Exists(candidate)) return candidate;
            }
            // 无扩展名直接命中（如 sh 类脚本）
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private static bool IsStoreAlias(string path)
        => path.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseVersion(string s, out Version v)
    {
        // 截取以数字开头的部分，容忍 "3.12.10"、"21.0.1" 等
        var m = Regex.Match(s, @"^\d+(\.\d+){0,3}");
        if (m.Success && Version.TryParse(m.Value, out var parsed))
        {
            v = parsed;
            return true;
        }
        v = new Version(0, 0);
        return false;
    }

    /// <summary>供 UI 展示的检测摘要</summary>
    public static string DescribePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "-";
        return path.Length > 90 ? path[..90] + "..." : path;
    }
}
