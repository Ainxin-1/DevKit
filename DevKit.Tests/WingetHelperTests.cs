using DevKit.Core;

namespace DevKit.Tests;

/// <summary>winget 候选评分测试</summary>
public class WingetHelperTests
{
    [Fact]
    public void SingleCandidate_ShouldBeConfident()
    {
        var candidates = new List<WingetHelper.PackageInfo>
        {
            new() { Name = "Git", Id = "Git.Git", Source = "winget" }
        };
        var (best, confident) = WingetHelper.SelectBestMatch("Git", candidates);
        Assert.NotNull(best);
        Assert.True(confident);
        Assert.Equal("Git.Git", best.Id);
    }

    [Fact]
    public void ExactIdMatch_ShouldBeSelected()
    {
        var candidates = new List<WingetHelper.PackageInfo>
        {
            new() { Name = "Git Extensions", Id = "GitExtensions.GitExtensions", Source = "winget" },
            new() { Name = "Git", Id = "Git.Git", Source = "winget" },
            new() { Name = "GitKraken", Id = "Axosoft.GitKraken", Source = "winget" }
        };
        var (best, confident) = WingetHelper.SelectBestMatch("Git", candidates);
        Assert.NotNull(best);
        Assert.Equal("Git.Git", best.Id);
    }

    [Fact]
    public void AmbiguousCandidates_ShouldNotBeConfident()
    {
        // 多个候选都包含 "Node" 但都不是精确匹配，且分数接近
        var candidates = new List<WingetHelper.PackageInfo>
        {
            new() { Name = "Node.js LTS", Id = "OpenJS.NodeJS.LTS", Source = "winget" },
            new() { Name = "Node.js Current", Id = "OpenJS.NodeJS", Source = "winget" },
            new() { Name = "NodeRed", Id = "NodeRed.NodeRed", Source = "winget" }
        };
        var (best, confident) = WingetHelper.SelectBestMatch("Node", candidates);
        // 多个候选分数接近时不应自动安装
        Assert.False(confident);
    }

    [Fact]
    public void EmptyCandidates_ShouldReturnNull()
    {
        var (best, confident) = WingetHelper.SelectBestMatch("Nothing", new List<WingetHelper.PackageInfo>());
        Assert.Null(best);
        Assert.False(confident);
    }

    [Fact]
    public void OfficialPublisher_ShouldScoreHigher()
    {
        var candidates = new List<WingetHelper.PackageInfo>
        {
            new() { Name = "Python", Id = "Python.Python.3.12", Source = "winget" },
            new() { Name = "Python Launcher", Id = "ThirdParty.Python", Source = "winget" }
        };
        var (best, confident) = WingetHelper.SelectBestMatch("Python", candidates);
        Assert.NotNull(best);
        Assert.Equal("Python.Python.3.12", best.Id);
    }
}
