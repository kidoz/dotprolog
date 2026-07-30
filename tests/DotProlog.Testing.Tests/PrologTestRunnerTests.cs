namespace DotProlog.Testing.Tests;

/// <summary>Discovering and running <c>test_*</c> predicates, including the ways a test can go wrong.</summary>
public sealed class PrologTestRunnerTests
{
    private static PrologTestRunner CreateRunner(string source) => new([("tests.pl", source)]);

    private static PrologTestResult RunSingle(PrologTestRunner runner)
    {
        PrologTest test = Assert.Single(runner.Discover());
        return runner.Run(test);
    }

    [Fact]
    public void DiscoversTestPredicatesInDeclarationOrder()
    {
        PrologTestRunner runner = CreateRunner("test_b. test_a. helper.");

        Assert.Equal(["test_b", "test_a"], runner.Discover().Select(test => test.Name));
    }

    [Fact]
    public void APassingTestReportsItsOutput()
    {
        PrologTestResult result = RunSingle(CreateRunner("test_pass :- write(hello)."));

        Assert.True(result.Succeeded);
        Assert.Equal("hello", result.Output);
    }

    [Fact]
    public void ALoopingTestTimesOutAndFailsInsteadOfHanging()
    {
        PrologTestRunner runner = CreateRunner("test_loop :- test_loop.");
        runner.Timeout = TimeSpan.FromMilliseconds(250);

        PrologTestResult result = RunSingle(runner);

        Assert.False(result.Succeeded);
        Assert.Contains("did not complete", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UserErrorOutputIsCapturedIntoTheFailure()
    {
        PrologTestResult result = RunSingle(CreateRunner("test_err :- write(user_error, oops), fail."));

        Assert.False(result.Succeeded);
        Assert.Contains("oops", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadTimeDirectiveOutputBelongsToNoTest()
    {
        PrologTestResult result = RunSingle(CreateRunner(":- write(loading).\ntest_quiet."));

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.Output);
    }
}
