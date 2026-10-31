namespace DotProlog.Testing.Tests;

/// <summary>Discovering and running <c>test_*</c> predicates, including the ways a test can go wrong.</summary>
public sealed class PrologTestRunnerTests
{
    [Fact]
    public void TimeoutStopsTheWorkerAndDisposesItsStreams()
    {
        var path = Path.GetTempFileName().Replace("\\", "/", StringComparison.Ordinal);
        try
        {
            Compiler.PrologEngine? observed = null;
            var runner = new PrologTestRunner(token =>
            {
                var engine = new Compiler.PrologEngine();
                observed = engine;
                engine.Machine.CancellationToken = token;
                engine.ConsultOrThrow(
                    $"test_loop :- open('{path}',write,S,[alias(file)]), write(S,buffered), loop. loop :- loop.",
                    "tests.pl"
                );
                return engine;
            })
            {
                Timeout = TimeSpan.FromMilliseconds(250),
            };

            PrologTestResult result = runner.Run(new PrologTest("test_loop", 0));
            Assert.False(result.Succeeded);
            Assert.Contains("the worker stopped after cancellation", result.Message, StringComparison.Ordinal);
            Assert.NotNull(observed);
            Assert.False(observed.Machine.IsRunning);
            Assert.Null(observed.Machine.Streams.ByAlias("file"));
            Assert.Equal("buffered", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoopingInitializationIsTimedDuringDiscoveryAndExecution()
    {
        var runner = CreateRunner("loop :- loop. :- initialization(loop). test_ok.");
        runner.Timeout = TimeSpan.FromMilliseconds(250);
        TimeoutException discovery = Assert.Throws<TimeoutException>(() => runner.Discover());
        Assert.Contains("worker stopped after cancellation", discovery.Message, StringComparison.Ordinal);
        PrologTestResult result = runner.Run(new PrologTest("test_ok", 0));
        Assert.False(result.Succeeded);
        Assert.Contains("worker stopped after cancellation", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HostFactoryFailuresAreReportedOnTheCallingThread()
    {
        var runner = new PrologTestRunner(() => throw new InvalidOperationException("factory failed"));
        Assert.Equal("factory failed", Assert.Throws<InvalidOperationException>(() => runner.Discover()).Message);
    }

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
        // Discovery runs under the default timeout: the short one below is meant for the looping
        // test body, and on a loaded CI machine engine startup alone can take longer than it.
        PrologTest test = Assert.Single(runner.Discover());
        runner.Timeout = TimeSpan.FromMilliseconds(250);

        PrologTestResult result = runner.Run(test);

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
