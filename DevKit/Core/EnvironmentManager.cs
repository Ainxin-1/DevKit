using System.IO;
using System.Text.Json;
using DevKit.Models;

namespace DevKit.Core;

/// <summary>
/// 环境导出/导入：把当前已安装的开发环境导出为 JSON，在新机器上一键还原。
/// 借鉴 DevBox 的 devbox.json 思路，但适配 Windows + winget/scoop 生态。
/// </summary>
public static class EnvironmentManager
{
    /// <summary>导出的环境配置结构</summary>
    public class EnvironmentFile
    {
        public string Version { get; set; } = "1.0";
        public string ExportedAt { get; set; } = "";
        public string MachineName { get; set; } = "";
        public List<EnvironmentEntry> Tools { get; set; } = new();
    }

    public class EnvironmentEntry
    {
        public string Name { get; set; } = "";
        public string? Version { get; set; }
        public string Category { get; set; } = "";
        public string? InstallMethod { get; set; }
        public string? InstallPath { get; set; }
    }

    /// <summary>导出当前已安装的环境到文件</summary>
    public static string Export(IEnumerable<ToolDetection> detections, string filePath)
    {
        var env = new EnvironmentFile
        {
            ExportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            MachineName = Environment.MachineName,
            Tools = detections
                .Where(d => d.IsInstalled)
                .Select(d => new EnvironmentEntry
                {
                    Name = d.Name,
                    Version = d.Version,
                    Category = d.Tool.Category,
                    InstallMethod = d.Tool.Install?.EffectiveMethods.FirstOrDefault()?.Method,
                    InstallPath = d.InstallPath
                })
                .ToList()
        };

        var json = JsonSerializer.Serialize(env, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
        Logger.Info($"环境已导出到 {filePath}，共 {env.Tools.Count} 个软件");
        return filePath;
    }

    /// <summary>导入模式</summary>
    public enum ImportMode
    {
        /// <summary>补全模式：只保证软件存在，不强制版本</summary>
        EnsurePresent,
        /// <summary>版本复现模式：检查当前版本与目标版本，版本不同时提示用户</summary>
        VersionMatch
    }

    /// <summary>导入结果项</summary>
    public class ImportResultItem
    {
        public string Name { get; set; } = "";
        public string? TargetVersion { get; set; }
        public string? CurrentVersion { get; set; }
        public bool NeedInstall { get; set; }
        public bool VersionMismatch { get; set; }
    }

    /// <summary>从文件导入环境，返回需要安装的软件名列表（补全模式）</summary>
    public static List<string> Import(string filePath, IReadOnlyDictionary<string, ToolDetection> detections)
    {
        return ImportWithMode(filePath, detections, ImportMode.EnsurePresent)
            .Where(r => r.NeedInstall)
            .Select(r => r.Name)
            .ToList();
    }

    /// <summary>从文件导入环境，支持补全模式和版本复现模式</summary>
    public static List<ImportResultItem> ImportWithMode(
        string filePath,
        IReadOnlyDictionary<string, ToolDetection> detections,
        ImportMode mode)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("环境文件不存在", filePath);

        var json = File.ReadAllText(filePath);
        var env = JsonSerializer.Deserialize<EnvironmentFile>(json);
        if (env?.Tools == null)
            throw new InvalidDataException("环境文件格式无效");

        var results = new List<ImportResultItem>();
        foreach (var entry in env.Tools)
        {
            var item = new ImportResultItem
            {
                Name = entry.Name,
                TargetVersion = entry.Version
            };

            if (detections.TryGetValue(entry.Name, out var det) && det.IsInstalled)
            {
                item.CurrentVersion = det.Version;
                item.NeedInstall = false;

                if (mode == ImportMode.VersionMatch && !string.IsNullOrEmpty(entry.Version))
                {
                    // 版本比较：主版本号不同则标记为不匹配
                    item.VersionMismatch = !VersionsCompatible(entry.Version, det.Version);
                    if (item.VersionMismatch)
                    {
                        Logger.Info($"版本不匹配: {entry.Name} 目标={entry.Version} 当前={det.Version}");
                    }
                }
            }
            else
            {
                item.NeedInstall = true;
            }

            results.Add(item);
        }

        var installCount = results.Count(r => r.NeedInstall);
        var mismatchCount = results.Count(r => r.VersionMismatch);
        Logger.Info($"环境导入（{mode}）：{env.Tools.Count} 个软件，{installCount} 个需安装，{mismatchCount} 个版本不匹配");
        return results;
    }

    /// <summary>判断版本是否兼容（主版本号相同即兼容）</summary>
    private static bool VersionsCompatible(string target, string? current)
    {
        if (string.IsNullOrEmpty(current)) return false;
        // 取主版本号比较
        var targetMajor = GetMajorVersion(target);
        var currentMajor = GetMajorVersion(current);
        return targetMajor == currentMajor;
    }

    private static string GetMajorVersion(string v)
    {
        var m = System.Text.RegularExpressions.Regex.Match(v, @"^(\d+)");
        return m.Success ? m.Value : v;
    }
}
