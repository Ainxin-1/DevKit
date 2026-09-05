using DevKit.Core;
using DevKit.Models;

namespace DevKit.Tests;

/// <summary>InstallEngine 测试（用 bundled 方式避免执行外部命令）</summary>
public class InstallEngineTests
{
    private static ToolInfo MakeTool(string name, string method = "bundled", string? requires = null)
    {
        var methods = new List<InstallMethodConfig>();
        var m = new InstallMethodConfig { Method = method };
        if (requires != null) m.Requires = requires;
        methods.Add(m);
        return new ToolInfo
        {
            Name = name,
            Category = "environment",
            Detect = new DetectConfig { Command = name.ToLower() },
            Install = new InstallConfig { Methods = methods }
        };
    }

    private static Dictionary<string, ToolDetection> MakeDetections(params ToolInfo[] tools)
    {
        var dict = new Dictionary<string, ToolDetection>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tools)
        {
            dict[t.Name] = new ToolDetection { Tool = t, Status = DetectionStatus.NotInstalled };
        }
        return dict;
    }

    [Fact]
    public void CircularRequirement_ShouldNotInfiniteLoop()
    {
        // A requires B, B requires A
        var a = MakeTool("A", requires: "B");
        var b = MakeTool("B", requires: "A");
        var all = new List<ToolInfo> { a, b };
        var detections = MakeDetections(a, b);

        var engine = new InstallEngine(_ => { }, all, detections);
        var plan = new List<InstallPlanItem>
        {
            new() { Tool = a, IsUserSelected = true, Reason = "test" }
        };

        // 不应死循环，应在合理时间内返回
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        engine.Execute(plan, cts.Token);

        // A 的安装应该失败（依赖循环）
        Assert.Equal(InstallResultStatus.DependencyFailed, detections["A"].InstallStatus);
        Assert.NotNull(detections["A"].InstallResult);
        Assert.Contains("循环", detections["A"].InstallResult);
    }

    [Fact]
    public void MissingRequirement_ShouldFail()
    {
        // A requires NonExistent
        var a = MakeTool("A", requires: "NonExistent");
        var all = new List<ToolInfo> { a };
        var detections = MakeDetections(a);

        var engine = new InstallEngine(_ => { }, all, detections);
        var plan = new List<InstallPlanItem>
        {
            new() { Tool = a, IsUserSelected = true, Reason = "test" }
        };

        engine.Execute(plan, CancellationToken.None);

        Assert.Equal(InstallResultStatus.DependencyFailed, detections["A"].InstallStatus);
    }

    [Fact]
    public void BundledInstall_ShouldDependOnRedetection()
    {
        // bundled 方式返回成功，但最终结果由复检决定
        // 测试用的命令 "a" 不存在，所以复检会失败
        var a = MakeTool("A");
        var all = new List<ToolInfo> { a };
        var detections = MakeDetections(a);

        var engine = new InstallEngine(_ => { }, all, detections);
        var plan = new List<InstallPlanItem>
        {
            new() { Tool = a, IsUserSelected = true, Reason = "test" }
        };

        engine.Execute(plan, CancellationToken.None);

        // bundled 命令执行成功，但复检未检测到命令 → DetectionFailed
        Assert.Equal(InstallResultStatus.DetectionFailed, detections["A"].InstallStatus);
        Assert.Contains("复检未检测到", detections["A"].InstallResult);
    }

    [Fact]
    public void Cancellation_BeforeExecute_ShouldThrow()
    {
        var a = MakeTool("A");
        var all = new List<ToolInfo> { a };
        var detections = MakeDetections(a);

        var engine = new InstallEngine(_ => { }, all, detections);
        var plan = new List<InstallPlanItem>
        {
            new() { Tool = a, IsUserSelected = true, Reason = "test" }
        };

        var cts = new CancellationTokenSource();
        cts.Cancel(); // 立即取消

        // Execute 在循环前检查取消，应直接抛出
        Assert.Throws<OperationCanceledException>(() => engine.Execute(plan, cts.Token));
    }
}
