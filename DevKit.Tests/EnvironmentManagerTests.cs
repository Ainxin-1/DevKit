using System.IO;
using DevKit.Core;
using DevKit.Models;

namespace DevKit.Tests;

/// <summary>
/// 测试 EnvironmentManager 的导出/导入功能。
/// </summary>
public class EnvironmentManagerTests : IDisposable
{
    private readonly string _testDir;

    public EnvironmentManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"DevKitEnvTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    private static ToolDetection MakeDetection(string name, bool installed, string? version = null)
    {
        return new ToolDetection
        {
            Tool = new ToolInfo
            {
                Name = name,
                Category = "environment",
                Install = new InstallConfig { Methods = new() { new() { Method = "winget", Id = "test" } } }
            },
            Status = installed ? DetectionStatus.Installed : DetectionStatus.NotInstalled,
            Version = version
        };
    }

    [Fact]
    public void Export_ShouldOnlyIncludeInstalledTools()
    {
        var detections = new List<ToolDetection>
        {
            MakeDetection("Git", true, "2.45.0"),
            MakeDetection("Python", false),
            MakeDetection("Node.js", true, "20.0.0"),
        };
        var filePath = Path.Combine(_testDir, "env.json");

        EnvironmentManager.Export(detections, filePath);

        Assert.True(File.Exists(filePath));
        var content = File.ReadAllText(filePath);
        Assert.Contains("Git", content);
        Assert.Contains("Node.js", content);
        Assert.DoesNotContain("\"Python\"", content); // 未安装的不应该在导出里
    }

    [Fact]
    public void Export_ShouldIncludeVersion()
    {
        var detections = new List<ToolDetection>
        {
            MakeDetection("Git", true, "2.45.0"),
        };
        var filePath = Path.Combine(_testDir, "env.json");

        EnvironmentManager.Export(detections, filePath);

        var content = File.ReadAllText(filePath);
        Assert.Contains("2.45.0", content);
    }

    [Fact]
    public void Import_ShouldReturnOnlyNotInstalledTools()
    {
        // 先导出
        var detections = new List<ToolDetection>
        {
            MakeDetection("Git", true, "2.45.0"),
            MakeDetection("Python", true, "3.12.0"),
        };
        var filePath = Path.Combine(_testDir, "env.json");
        EnvironmentManager.Export(detections, filePath);

        // 模拟当前状态：Git 已装，Python 未装
        var current = new Dictionary<string, ToolDetection>
        {
            ["Git"] = MakeDetection("Git", true),
            ["Python"] = MakeDetection("Python", false),
        };

        var toInstall = EnvironmentManager.Import(filePath, current);
        Assert.Single(toInstall);
        Assert.Contains("Python", toInstall);
    }

    [Fact]
    public void Import_AllInstalled_ShouldReturnEmpty()
    {
        var detections = new List<ToolDetection>
        {
            MakeDetection("Git", true),
        };
        var filePath = Path.Combine(_testDir, "env.json");
        EnvironmentManager.Export(detections, filePath);

        var current = new Dictionary<string, ToolDetection>
        {
            ["Git"] = MakeDetection("Git", true),
        };

        var toInstall = EnvironmentManager.Import(filePath, current);
        Assert.Empty(toInstall);
    }

    [Fact]
    public void Import_NonExistentFile_ShouldThrow()
    {
        Assert.Throws<FileNotFoundException>(() =>
            EnvironmentManager.Import(Path.Combine(_testDir, "nonexistent.json"), new Dictionary<string, ToolDetection>()));
    }

    [Fact]
    public void Export_ShouldIncludeMachineName()
    {
        var detections = new List<ToolDetection> { MakeDetection("Git", true) };
        var filePath = Path.Combine(_testDir, "env.json");
        EnvironmentManager.Export(detections, filePath);

        var content = File.ReadAllText(filePath);
        Assert.Contains(Environment.MachineName, content);
    }

    [Fact]
    public void Export_ShouldIncludeTimestamp()
    {
        var detections = new List<ToolDetection> { MakeDetection("Git", true) };
        var filePath = Path.Combine(_testDir, "env.json");
        EnvironmentManager.Export(detections, filePath);

        var content = File.ReadAllText(filePath);
        Assert.Contains("ExportedAt", content);
    }
}
