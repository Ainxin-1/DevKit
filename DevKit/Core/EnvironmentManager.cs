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

    /// <summary>从文件导入环境，返回需要安装的软件名列表</summary>
    public static List<string> Import(string filePath, IReadOnlyDictionary<string, ToolDetection> detections)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("环境文件不存在", filePath);

        var json = File.ReadAllText(filePath);
        var env = JsonSerializer.Deserialize<EnvironmentFile>(json);
        if (env?.Tools == null)
            throw new InvalidDataException("环境文件格式无效");

        var toInstall = new List<string>();
        foreach (var entry in env.Tools)
        {
            if (detections.TryGetValue(entry.Name, out var det) && !det.IsInstalled)
            {
                toInstall.Add(entry.Name);
            }
        }
        Logger.Info($"环境导入：{env.Tools.Count} 个软件中，{toInstall.Count} 个需要安装");
        return toInstall;
    }
}
