using System.IO;
using DevKit.Core;

namespace DevKit.Tests;

/// <summary>
/// 测试 ProjectDetector 能否正确识别各种项目类型。
/// 通过创建临时目录和模拟项目文件来验证。
/// </summary>
public class ProjectDetectorTests : IDisposable
{
    private readonly string _testDir;

    public ProjectDetectorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"DevKitTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public void Detect_EmptyDirectory_ShouldReturnNoTools()
    {
        var result = ProjectDetector.Detect(_testDir);
        Assert.Empty(result.RequiredTools);
    }

    [Fact]
    public void Detect_PackageJson_ShouldRequireNodeAndNpm()
    {
        File.WriteAllText(Path.Combine(_testDir, "package.json"), "{}");
        var result = ProjectDetector.Detect(_testDir);
        Assert.Contains("Node.js", result.RequiredTools);
        Assert.Contains("npm", result.RequiredTools);
    }

    [Fact]
    public void Detect_RequirementsTxt_ShouldRequirePythonAndPip()
    {
        File.WriteAllText(Path.Combine(_testDir, "requirements.txt"), "flask==2.0");
        var result = ProjectDetector.Detect(_testDir);
        Assert.Contains("Python", result.RequiredTools);
        Assert.Contains("pip", result.RequiredTools);
    }

    [Fact]
    public void Detect_GoMod_ShouldRequireGo()
    {
        File.WriteAllText(Path.Combine(_testDir, "go.mod"), "module example.com/app");
        var result = ProjectDetector.Detect(_testDir);
        Assert.Contains("Go", result.RequiredTools);
    }

    [Fact]
    public void Detect_PomXml_ShouldRequireJdkAndMaven()
    {
        File.WriteAllText(Path.Combine(_testDir, "pom.xml"), "<project></project>");
        var result = ProjectDetector.Detect(_testDir);
        Assert.Contains("JDK", result.RequiredTools);
        Assert.Contains("Maven", result.RequiredTools);
    }

    [Fact]
    public void Detect_CargoToml_ShouldRequireRustAndCargo()
    {
        File.WriteAllText(Path.Combine(_testDir, "Cargo.toml"), "[package]");
        var result = ProjectDetector.Detect(_testDir);
        Assert.Contains("Rust", result.RequiredTools);
        Assert.Contains("Cargo", result.RequiredTools);
    }

    [Fact]
    public void Detect_PubspecYaml_ShouldRequireFlutter()
    {
        File.WriteAllText(Path.Combine(_testDir, "pubspec.yaml"), "name: app");
        var result = ProjectDetector.Detect(_testDir);
        Assert.Contains("Flutter", result.RequiredTools);
    }

    [Fact]
    public void Detect_CMakeLists_ShouldRequireCMakeAndMinGW()
    {
        File.WriteAllText(Path.Combine(_testDir, "CMakeLists.txt"), "cmake_minimum_required(VERSION 3.10)");
        var result = ProjectDetector.Detect(_testDir);
        Assert.Contains("CMake", result.RequiredTools);
        Assert.Contains("MinGW", result.RequiredTools);
    }

    [Fact]
    public void Detect_Csproj_ShouldRequireDotNetSdk()
    {
        File.WriteAllText(Path.Combine(_testDir, "MyApp.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        var result = ProjectDetector.Detect(_testDir);
        Assert.Contains(".NET SDK", result.RequiredTools);
    }

    [Fact]
    public void Detect_Dockerfile_ShouldRequireDocker()
    {
        File.WriteAllText(Path.Combine(_testDir, "Dockerfile"), "FROM ubuntu:latest");
        var result = ProjectDetector.Detect(_testDir);
        Assert.Contains("Docker Desktop", result.RequiredTools);
    }

    [Fact]
    public void Detect_ComposerJson_ShouldRequirePhpAndComposer()
    {
        File.WriteAllText(Path.Combine(_testDir, "composer.json"), "{}");
        var result = ProjectDetector.Detect(_testDir);
        Assert.Contains("PHP", result.RequiredTools);
        Assert.Contains("Composer", result.RequiredTools);
    }

    [Fact]
    public void Detect_PnpmLockYaml_ShouldRequirePnpm()
    {
        File.WriteAllText(Path.Combine(_testDir, "package.json"), "{}");
        File.WriteAllText(Path.Combine(_testDir, "pnpm-lock.yaml"), "lockfileVersion: '6.0'");
        var result = ProjectDetector.Detect(_testDir);
        Assert.Contains("pnpm", result.RequiredTools);
    }

    [Fact]
    public void Detect_UvLock_ShouldRequireUv()
    {
        File.WriteAllText(Path.Combine(_testDir, "uv.lock"), "version = 1");
        var result = ProjectDetector.Detect(_testDir);
        Assert.Contains("uv", result.RequiredTools);
    }

    [Fact]
    public void Detect_MultipleProjectTypes_ShouldUnionRequirements()
    {
        File.WriteAllText(Path.Combine(_testDir, "package.json"), "{}");
        File.WriteAllText(Path.Combine(_testDir, "go.mod"), "module app");
        var result = ProjectDetector.Detect(_testDir);
        Assert.Contains("Node.js", result.RequiredTools);
        Assert.Contains("Go", result.RequiredTools);
    }

    [Fact]
    public void Detect_ShouldNotDescendIntoNodeModules()
    {
        // 在 node_modules 里放一个 go.mod，不应该被检测到
        var nmDir = Path.Combine(_testDir, "node_modules");
        Directory.CreateDirectory(nmDir);
        File.WriteAllText(Path.Combine(nmDir, "go.mod"), "module app");
        File.WriteAllText(Path.Combine(_testDir, "package.json"), "{}");

        var result = ProjectDetector.Detect(_testDir);
        Assert.DoesNotContain("Go", result.RequiredTools);
    }
}
