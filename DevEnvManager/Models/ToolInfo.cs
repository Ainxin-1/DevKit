using System.Text.Json.Serialization;

namespace DevEnvManager.Models;

/// <summary>
/// 单个软件的配置项（来自 config/tools.json）。
/// 新增软件时只需在 JSON 中增加一项，无需修改程序代码。
/// </summary>
public class ToolInfo
{
    /// <summary>显示名称，如 "Python"</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>分类：environment | package_manager</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "environment";

    /// <summary>子分类：common | uncommon</summary>
    [JsonPropertyName("subcategory")]
    public string Subcategory { get; set; } = "common";

    /// <summary>中文说明</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>检测配置</summary>
    [JsonPropertyName("detect")]
    public DetectConfig? Detect { get; set; }

    /// <summary>依赖的软件名称列表，如 pip 依赖 Python</summary>
    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();

    /// <summary>安装配置</summary>
    [JsonPropertyName("install")]
    public InstallConfig? Install { get; set; }

    [JsonIgnore]
    public ToolCategory CategoryEnum =>
        Category.Equals("package_manager", StringComparison.OrdinalIgnoreCase)
            ? ToolCategory.PackageManager
            : ToolCategory.Environment;

    [JsonIgnore]
    public ToolSubcategory SubcategoryEnum =>
        Subcategory.Equals("uncommon", StringComparison.OrdinalIgnoreCase)
            ? ToolSubcategory.Uncommon
            : ToolSubcategory.Common;
}

/// <summary>检测配置</summary>
public class DetectConfig
{
    /// <summary>检测命令名（在 PATH 中查找），如 "python"</summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    /// <summary>获取版本用的参数，如 "--version"；java 用 "-version"（输出到 stderr）</summary>
    [JsonPropertyName("versionArgs")]
    public string VersionArgs { get; set; } = "--version";

    /// <summary>从版本输出中提取版本号的正则，默认提取第一个 x.y[.z]</summary>
    [JsonPropertyName("versionRegex")]
    public string VersionRegex { get; set; } = @"(\d+\.\d+(\.\d+)?)";

    /// <summary>最低可接受版本（低于则标记"版本过低"），可为空</summary>
    [JsonPropertyName("minVersion")]
    public string? MinVersion { get; set; }

    /// <summary>常见安装路径线索（用于路径检测的补充）</summary>
    [JsonPropertyName("pathHints")]
    public List<string> PathHints { get; set; } = new();

    /// <summary>环境变量名（如 ANDROID_HOME），存在则视为已安装</summary>
    [JsonPropertyName("envVar")]
    public string? EnvVar { get; set; }
}

/// <summary>安装配置</summary>
public class InstallConfig
{
    /// <summary>安装方式：winget | bundled | official | manual</summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = "winget";

    /// <summary>winget 包 ID，如 "Python.Python.3.12"</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>备用 winget 包 ID（主 ID 查询失败时尝试）</summary>
    [JsonPropertyName("fallbackId")]
    public string? FallbackId { get; set; }

    /// <summary>官方命令（method=official 时使用），如 pip 的 "python -m ensurepip --upgrade"</summary>
    [JsonPropertyName("officialCommand")]
    public string? OfficialCommand { get; set; }

    /// <summary>官方下载/引导链接（method=manual 时展示给用户）</summary>
    [JsonPropertyName("manualUrl")]
    public string? ManualUrl { get; set; }

    [JsonIgnore]
    public InstallMethod MethodEnum =>
        Method switch
        {
            "bundled" => InstallMethod.Bundled,
            "official" => InstallMethod.Official,
            "manual" => InstallMethod.Manual,
            _ => InstallMethod.Winget
        };
}
