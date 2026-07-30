using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Requests;

namespace DotProlog.Testing.Tests;

/// <summary>The framework-level plumbing the platform depends on, tested without a test host.</summary>
public sealed class PrologTestFrameworkTests
{
    [Fact]
    public void NoFilterSelectsEveryTest()
    {
        Assert.True(PrologTestFramework.ShouldRun(null, "prolog:test_a/0"));
    }

    [Fact]
    public void AUidListFilterSelectsOnlyItsTests()
    {
        var filter = new TestNodeUidListFilter([new TestNodeUid("prolog:test_a/0")]);

        Assert.True(PrologTestFramework.ShouldRun(filter, "prolog:test_a/0"));
        Assert.False(PrologTestFramework.ShouldRun(filter, "prolog:test_b/0"));
    }
}
