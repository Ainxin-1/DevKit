using System.IO;
using System.Text.Json;
using DevKit.Core;
using DevKit.Models;

namespace DevKit.Tests;

/// <summary>
/// 验证 tools.json 配置文件的格式正确性、完整性和一致性。
/// 这是最重要的测试——配置错误会导致整个软件列表异常。
/// </summary>
public class ToolsJsonTests
{
    private readonly List<ToolInfo> _tools;

    public ToolsJsonTests()
    {
        // 直接读取 DevKit 项目的 config/tools.json（不依赖运行时路径）
        var configPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "DevKit", "config", "tools.json");
        var json = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var root = JsonSerializer.Deserialize<ToolsRoot>(json, options);
        _tools = root?.Tools ?? new List<ToolInfo>();
    }

    private class ToolsRoot
    {
        public List<ToolInfo>? Tools { get; set; }
    }

    [Fact]
    public void Load_ShouldReturnNonEmptyList()
    {
        Assert.NotEmpty(_tools);
        Assert.True(_tools.Count >= 40, $"软件数量过少：{_tools.Count}");
    }

    [Fact]
    public void AllTools_ShouldHaveRequiredFields()
    {
        foreach (var tool in _tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Name), $"软件名称为空");
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{tool.Name} 描述为空");
            Assert.True(tool.Category is "environment" or "package_manager",
                $"{tool.Name} 分类无效: {tool.Category}");
            Assert.NotNull(tool.Detect);
            Assert.False(string.IsNullOrWhiteSpace(tool.Detect.Command), $"{tool.Name} 检测命令为空");
        }
    }

    [Fact]
    public void AllTools_ShouldHaveAtLeastTwoInstallMethods()
    {
        foreach (var tool in _tools)
        {
            Assert.NotNull(tool.Install);
            var methods = tool.Install.EffectiveMethods;
            // bundled 方式的软件随宿主安装，允许只有 1 种方式
            var isBundledOnly = methods.Count == 1 && methods[0].Method == "bundled";
            Assert.True(methods.Count >= 2 || isBundledOnly,
                $"{tool.Name} 安装方式少于2种：{methods.Count}（非bundled）");
        }
    }

    [Fact]
    public void AllTools_ShouldHaveUniqueNames()
    {
        var names = _tools.Select(t => t.Name).ToList();
        var duplicates = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void AllDependencies_ShouldReferenceExistingTools()
    {
        var nameSet = _tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in _tools)
        {
            if (tool.Dependencies == null) continue;
            foreach (var dep in tool.Dependencies)
            {
                Assert.True(nameSet.Contains(dep),
                    $"{tool.Name} 依赖了不存在的软件: {dep}");
            }
        }
    }

    [Fact]
    public void AllRequires_ShouldReferenceExistingTools()
    {
        var nameSet = _tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in _tools)
        {
            if (tool.Install?.Methods == null) continue;
            foreach (var method in tool.Install.Methods)
            {
                if (!string.IsNullOrEmpty(method.Requires))
                {
                    Assert.True(nameSet.Contains(method.Requires),
                        $"{tool.Name} 的安装方式 requires 了不存在的软件: {method.Requires}");
                }
            }
        }
    }

    [Fact]
    public void CategoryCounts_ShouldBeReasonable()
    {
        var envCount = _tools.Count(t => t.Category == "environment");
        var pmCount = _tools.Count(t => t.Category == "package_manager");
        Assert.True(envCount >= 15, $"开发环境数量过少: {envCount}");
        Assert.True(pmCount >= 15, $"包管理器数量过少: {pmCount}");
    }

    [Fact]
    public void WingetMethod_ShouldHaveId()
    {
        foreach (var tool in _tools)
        {
            if (tool.Install?.Methods == null) continue;
            foreach (var method in tool.Install.Methods.Where(m => m.Method == "winget"))
            {
                Assert.False(string.IsNullOrWhiteSpace(method.Id),
                    $"{tool.Name} 的 winget 方式缺少包 ID");
            }
        }
    }

    [Fact]
    public void ScoopMethod_ShouldHaveId()
    {
        foreach (var tool in _tools)
        {
            if (tool.Install?.Methods == null) continue;
            foreach (var method in tool.Install.Methods.Where(m => m.Method == "scoop"))
            {
                Assert.False(string.IsNullOrWhiteSpace(method.Id),
                    $"{tool.Name} 的 scoop 方式缺少包名");
            }
        }
    }
}
