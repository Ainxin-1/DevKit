using System.IO;
using System.Text.Json;
using DevKit.Models;

namespace DevKit.Core;

/// <summary>
/// 从 config/tools.json 加载软件清单。用户可直接编辑 JSON 扩展软件。
/// </summary>
public static class ConfigLoader
{
    /// <summary>配置文件名（位于程序目录 config/ 下）</summary>
    public const string ConfigFileName = "tools.json";

    public static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "config", ConfigFileName);

    /// <summary>加载全部软件配置</summary>
    public static List<ToolInfo> Load()
    {
        var path = ConfigPath;
        if (!File.Exists(path))
        {
            Logger.Error($"配置文件不存在: {path}");
            return new List<ToolInfo>();
        }

        try
        {
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            var root = JsonSerializer.Deserialize<ToolsRoot>(json, options);
            var tools = root?.Tools ?? new List<ToolInfo>();
            Logger.Info($"已加载配置 {path}，共 {tools.Count} 个软件");
            return tools;
        }
        catch (Exception ex)
        {
            Logger.Error($"配置文件解析失败: {ex.Message}");
            return new List<ToolInfo>();
        }
    }

    private class ToolsRoot
    {
        public List<ToolInfo>? Tools { get; set; }
    }
}
