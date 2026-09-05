using DevKit.Core;
using DevKit.Models;

namespace DevKit.Tests;

/// <summary>依赖解析器测试：拓扑排序、循环依赖检测</summary>
public class DependencyResolverTests
{
    private static ToolInfo MakeTool(string name, params string[] deps)
        => new() { Name = name, Dependencies = deps.ToList(), Category = "environment" };

    private static Dictionary<string, ToolInfo> Index(params ToolInfo[] tools)
        => tools.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void SimpleDependency_ShouldOrderDependencyFirst()
    {
        var a = MakeTool("A", "B");
        var b = MakeTool("B");
        var all = Index(a, b);
        var plan = DependencyResolver.BuildPlan(new[] { a }, all, _ => false, out var cycles);

        Assert.Empty(cycles);
        Assert.Equal(2, plan.Count);
        Assert.Equal("B", plan[0].Tool.Name); // 依赖在前
        Assert.Equal("A", plan[1].Tool.Name);
    }

    [Fact]
    public void ChainDependency_ShouldTopologicalOrder()
    {
        var a = MakeTool("A", "B");
        var b = MakeTool("B", "C");
        var c = MakeTool("C");
        var all = Index(a, b, c);
        var plan = DependencyResolver.BuildPlan(new[] { a }, all, _ => false, out var cycles);

        Assert.Empty(cycles);
        Assert.Equal(3, plan.Count);
        Assert.Equal("C", plan[0].Tool.Name);
        Assert.Equal("B", plan[1].Tool.Name);
        Assert.Equal("A", plan[2].Tool.Name);
    }

    [Fact]
    public void SharedDependency_ShouldNotDuplicate()
    {
        var a = MakeTool("A", "C");
        var b = MakeTool("B", "C");
        var c = MakeTool("C");
        var all = Index(a, b, c);
        var plan = DependencyResolver.BuildPlan(new[] { a, b }, all, _ => false, out var cycles);

        Assert.Empty(cycles);
        Assert.Equal(3, plan.Count);
        Assert.Single(plan, p => p.Tool.Name == "C");
    }

    [Fact]
    public void CircularDependency_ShouldBeDetected()
    {
        var a = MakeTool("A", "B");
        var b = MakeTool("B", "A");
        var all = Index(a, b);
        var plan = DependencyResolver.BuildPlan(new[] { a }, all, _ => false, out var cycles);

        Assert.NotEmpty(cycles);
        Assert.Contains("A", cycles[0].CyclePath);
        Assert.Contains("B", cycles[0].CyclePath);
    }

    [Fact]
    public void InstalledDependency_ShouldBeSkipped()
    {
        var a = MakeTool("A", "B");
        var b = MakeTool("B");
        var all = Index(a, b);
        var plan = DependencyResolver.BuildPlan(new[] { a }, all,
            name => name.Equals("B", StringComparison.OrdinalIgnoreCase), out var cycles);

        Assert.Empty(cycles);
        Assert.Single(plan);
        Assert.Equal("A", plan[0].Tool.Name);
    }

    [Fact]
    public void NoDependencies_ShouldReturnSelf()
    {
        var a = MakeTool("A");
        var all = Index(a);
        var plan = DependencyResolver.BuildPlan(new[] { a }, all, _ => false, out var cycles);

        Assert.Empty(cycles);
        Assert.Single(plan);
        Assert.True(plan[0].IsUserSelected);
    }
}
