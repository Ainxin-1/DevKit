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

    /// <summary>配置加载结果</summary>
    public class LoadResult
    {
        public List<ToolInfo> Tools { get; init; } = new();
        public bool Success { get; init; }
        public string? Error { get; init; }
        public bool IsEmpty => Success && Tools.Count == 0;
    }

    /// <summary>加载全部软件配置（带错误状态）</summary>
    public static LoadResult LoadWithStatus()
    {
        var path = ConfigPath;
        if (!File.Exists(path))
        {
            var msg = $"配置文件不存在: {path}";
            Logger.Error(msg);
            return new LoadResult { Success = false, Error = msg };
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

            // 配置校验
            var validationError = ValidateTools(tools);
            if (validationError != null)
            {
                Logger.Error($"配置校验失败: {validationError}");
                return new LoadResult { Success = false, Error = validationError, Tools = tools };
            }

            Logger.Info($"已加载配置 {path}，共 {tools.Count} 个软件");
            return new LoadResult { Success = true, Tools = tools };
        }
        catch (JsonException ex)
        {
            var msg = $"配置文件 JSON 格式错误: {ex.Message}";
            Logger.Error(msg);
            return new LoadResult { Success = false, Error = msg };
        }
        catch (Exception ex)
        {
            var msg = $"配置文件加载失败: {ex.Message}";
            Logger.Error(msg);
            return new LoadResult { Success = false, Error = msg };
        }
    }

    /// <summary>加载全部软件配置（兼容旧接口，失败返回空列表）</summary>
    public static List<ToolInfo> Load()
    {
        var result = LoadWithStatus();
        return result.Tools;
    }

    /// <summary>校验工具配置，返回错误信息（null 表示通过）</summary>
    private static string? ValidateTools(List<ToolInfo> tools)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name))
                return "存在名称为空的软件配置";
            if (!names.Add(tool.Name))
                return $"软件名称重复: {tool.Name}";
            if (tool.Detect == null)
                return $"软件 {tool.Name} 缺少 detect 配置";
            if (tool.Install == null)
                return $"软件 {tool.Name} 缺少 install 配置";
        }
        // 检查依赖引用
        foreach (var tool in tools)
        {
            foreach (var dep in tool.Dependencies)
            {
                if (!names.Contains(dep))
                    return $"软件 {tool.Name} 依赖不存在的软件: {dep}";
            }
        }
        return null;
    }

    private class ToolsRoot
    {
        public List<ToolInfo>? Tools { get; set; }
    }
}
