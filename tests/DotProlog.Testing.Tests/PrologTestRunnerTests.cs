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

            var result = runner.Run(new PrologTest("test_loop", 0));
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
        var discovery = Assert.Throws<TimeoutException>(() => runner.Discover());
        Assert.Contains("worker stopped after cancellation", discovery.Message, StringComparison.Ordinal);
        var result = runner.Run(new PrologTest("test_ok", 0));
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
        var test = Assert.Single(runner.Discover());
        return runner.Run(test);
    }

    [Fact]
    public void DiscoversTestPredicatesInDeclarationOrder()
    {
        var runner = CreateRunner("test_b. test_a. helper.");

        Assert.Equal(["test_b", "test_a"], runner.Discover().Select(test => test.Name));
    }

    [Fact]
    public void APassingTestReportsItsOutput()
    {
        var result = RunSingle(CreateRunner("test_pass :- write(hello)."));

        Assert.True(result.Succeeded);
        Assert.Equal("hello", result.Output);
    }

    [Fact]
    public void ALoopingTestTimesOutAndFailsInsteadOfHanging()
    {
        var runner = CreateRunner("test_loop :- test_loop.");
        // Discovery runs under the default timeout: the short one below is meant for the looping
        // test body, and on a loaded CI machine engine startup alone can take longer than it.
        var test = Assert.Single(runner.Discover());
        runner.Timeout = TimeSpan.FromMilliseconds(250);

        var result = runner.Run(test);

        Assert.False(result.Succeeded);
        Assert.Contains("did not complete", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UserErrorOutputIsCapturedIntoTheFailure()
    {
        var result = RunSingle(CreateRunner("test_err :- write(user_error, oops), fail."));

        Assert.False(result.Succeeded);
        Assert.Contains("oops", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadTimeDirectiveOutputBelongsToNoTest()
    {
        var result = RunSingle(CreateRunner(":- write(loading).\ntest_quiet."));

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public void NonExportedModuleTestsHaveUniqueQualifiedIdentities()
    {
        var runner = new PrologTestRunner([("a.pl", ":- module(a, []). test_same."), ("b.pl", ":- module(b, []). test_same.")]);

        var tests = runner.Discover();

        Assert.Equal(["a:test_same", "b:test_same"], tests.Select(test => test.Name));
        Assert.Equal(["prolog:a:test_same/0", "prolog:b:test_same/0"], tests.Select(test => test.Uid));
        Assert.All(tests, test => Assert.True(runner.Run(test).Succeeded));
    }

    [Fact]
    public void AnExportedModuleTestIsDiscoveredOnceUnderItsQualifiedName()
    {
        var runner = CreateRunner(":- module(m, [test_exported/0]). test_exported.");

        var test = Assert.Single(runner.Discover());

        Assert.Equal("m:test_exported", test.Name);
        Assert.Equal("prolog:m:test_exported/0", test.Uid);
        Assert.True(runner.Run(test).Succeeded);
    }

    [Theory]
    [InlineData(":- initialization(fail). test_ok.", "Test initialization failed")]
    [InlineData(":- initialization(halt(7)). test_ok.", "Test initialization halted with exit code 7")]
    public void InitializationFailureIsVisibleDuringDiscoveryAndExecution(string source, string expected)
    {
        var runner = CreateRunner(source);

        var discovery = Assert.Throws<Runtime.PrologException>(() => runner.Discover());
        Assert.Contains(expected, discovery.Message, StringComparison.Ordinal);
        var result = runner.Run(new PrologTest("test_ok", 0));
        Assert.False(result.Succeeded);
        Assert.Contains(expected, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FactoryFailureBecomesAFailedResultWithoutHidingDiscoveryFailure()
    {
        var runner = new PrologTestRunner(() => throw new InvalidOperationException("factory failed"));

        Assert.Equal("factory failed", Assert.Throws<InvalidOperationException>(() => runner.Discover()).Message);
        var result = runner.Run(new PrologTest("test_ok", 0));
        Assert.False(result.Succeeded);
        Assert.Contains("InvalidOperationException: factory failed", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HostPredicateFailureBecomesAFailedResultWithOutput()
    {
        var runner = new PrologTestRunner(() =>
        {
            var engine = new Compiler.PrologEngine();
            engine.Program.Builtins.Register("host_fault", 0, _ => throw new InvalidOperationException("host failed"));
            engine.ConsultOrThrow("test_host :- write(before), host_fault.", "tests.pl");
            return engine;
        });

        var result = RunSingle(runner);

        Assert.False(result.Succeeded);
        Assert.Contains("InvalidOperationException: host failed", result.Message, StringComparison.Ordinal);
        Assert.Equal("before", result.Output);
    }

    [Fact]
    public void CooperativeTimeoutRetainsStandardAndErrorOutput()
    {
        var runner = CreateRunner("test_loop :- write(before), write(user_error, problem), loop. loop :- loop.");
        var test = Assert.Single(runner.Discover());
        runner.Timeout = TimeSpan.FromMilliseconds(250);

        var result = runner.Run(test);

        Assert.False(result.Succeeded);
        Assert.Contains("worker stopped after cancellation", result.Message, StringComparison.Ordinal);
        Assert.Equal("before", result.Output);
        Assert.Equal("problem", result.Error);
    }

    [Fact]
    public void CancellationReachesDiscoveryAndExecutionFactories()
    {
        var runner = new PrologTestRunner(token =>
        {
            token.ThrowIfCancellationRequested();
            var engine = new Compiler.PrologEngine();
            engine.Machine.CancellationToken = token;
            engine.ConsultOrThrow("test_loop :- test_loop.", "tests.pl");
            return engine;
        });

        using var discoveryCancellation = new CancellationTokenSource();
        discoveryCancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => runner.DiscoverWithCancellation(discoveryCancellation.Token));

        PrologTest test = new("test_loop", 0);
        using var runCancellation = new CancellationTokenSource();
        runCancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => runner.RunWithCancellation(test, runCancellation.Token));
    }
}
