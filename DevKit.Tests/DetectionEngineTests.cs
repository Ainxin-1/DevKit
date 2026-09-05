using DevKit.Core;
using DevKit.Models;

namespace DevKit.Tests;

/// <summary>DetectionEngine 纯逻辑测试（不依赖外部命令）</summary>
public class DetectionEngineTests
{
    [Fact]
    public void DetermineStatus_Installed_WhenVersionOk()
    {
        var status = DetectionEngine.DetermineStatus("3.12.1", "3.0", out var msg);
        Assert.Equal(DetectionStatus.Installed, status);
        Assert.Null(msg);
    }

    [Fact]
    public void DetermineStatus_VersionTooLow_WhenVersionBelowMin()
    {
        var status = DetectionEngine.DetermineStatus("2.7.0", "3.0", out var msg);
        Assert.Equal(DetectionStatus.VersionTooLow, status);
        Assert.NotNull(msg);
        Assert.Contains("2.7.0", msg);
    }

    [Fact]
    public void DetermineStatus_VersionUnknown_WhenVersionEmpty()
    {
        var status = DetectionEngine.DetermineStatus(null, "3.0", out var msg);
        Assert.Equal(DetectionStatus.VersionUnknown, status);
        Assert.NotNull(msg);
        Assert.Contains("无法确认版本", msg);
    }

    [Fact]
    public void DetermineStatus_Installed_WhenNoMinVersion()
    {
        var status = DetectionEngine.DetermineStatus("1.0", null, out var msg);
        Assert.Equal(DetectionStatus.Installed, status);
    }

    [Fact]
    public void DetermineStatus_VersionUnknown_WhenVersionEmptyAndNoMin()
    {
        var status = DetectionEngine.DetermineStatus("", null, out var msg);
        Assert.Equal(DetectionStatus.VersionUnknown, status);
    }

    [Theory]
    [InlineData("3.12.10", true)]
    [InlineData("21.0.1", true)]
    [InlineData("1.2.3.4", true)]
    [InlineData("v1.0", false)] // 不以数字开头
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void TryParseVersion_VariousInputs(string input, bool shouldSucceed)
    {
        var ok = DetectionEngine.TryParseVersion(input, out var v);
        Assert.Equal(shouldSucceed, ok);
        if (shouldSucceed)
        {
            Assert.NotNull(v);
            Assert.True(v.Major >= 0);
        }
    }

    [Fact]
    public void FindExecutable_FindsCmd()
    {
        // cmd.exe 一定在系统 PATH 中
        var path = DetectionEngine.FindExecutable("cmd");
        Assert.NotNull(path);
        Assert.EndsWith("cmd.exe", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindExecutable_ReturnsNull_ForNonexistent()
    {
        var path = DetectionEngine.FindExecutable("this_tool_definitely_does_not_exist_12345");
        Assert.Null(path);
    }

    [Fact]
    public void FindExecutable_ReturnsNull_ForEmpty()
    {
        Assert.Null(DetectionEngine.FindExecutable(""));
        Assert.Null(DetectionEngine.FindExecutable("   "));
    }
}
