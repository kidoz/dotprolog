using System.Globalization;
using System.Runtime.ExceptionServices;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.Testing;

/// <summary>
/// Loads a test program and runs its test predicates.
/// </summary>
/// <remarks>
/// <para>
/// A test is any zero-arity predicate whose name begins with <c>test_</c>. Discovery by naming
/// convention rather than a declaration keeps a test file plain Prolog, loadable by any other system
/// — the same reasoning that keeps a <c>.pl</c> free of <c>clr_export</c>.
/// </para>
/// <para>
/// Each test gets a fresh engine, so one test cannot see clauses another asserted. Reloading the
/// generated program for each test is what makes a failure mean what it says.
/// </para>
/// </remarks>
public sealed class PrologTestRunner
{
    /// <summary>The prefix that marks a predicate as a test.</summary>
    public const string TestPrefix = "test_";

    /// <summary>The environment variable that overrides the per-test timeout, in whole seconds.</summary>
    public const string TimeoutVariable = "DOTPROLOG_TEST_TIMEOUT_SECONDS";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private readonly Func<CancellationToken, PrologEngine> _engineFactory;

    /// <summary>Creates a runner over the given Prolog sources.</summary>
    /// <param name="sources">Each source file's name and contents.</param>
    public PrologTestRunner(IReadOnlyList<(string Name, string Text)> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _engineFactory = cancellationToken =>
        {
            // Muted while loading: directive output belongs to no single test, and during
            // discovery it would repeat once per scan.
            var engine = new PrologEngine
            {
                Output = TextWriter.Null,
                Error = TextWriter.Null,
                Input = TextReader.Null,
            };
            engine.Machine.CancellationToken = cancellationToken;
            try
            {
                foreach ((var name, var text) in sources)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    engine.ConsultOrThrow(text, name);
                }

                engine.RunPendingGoals();
                return engine;
            }
            catch
            {
                engine.Dispose();
                throw;
            }
        };
    }

    /// <summary>Creates a runner over an engine factory.</summary>
    /// <remarks>
    /// The timeout covers factory invocation, but cancellation cannot reach its engine until it
    /// returns. Use the token-aware overload for factories that run Prolog during initialization.
    /// </remarks>
    public PrologTestRunner(Func<PrologEngine> engineFactory)
    {
        ArgumentNullException.ThrowIfNull(engineFactory);
        _engineFactory = _ => engineFactory();
    }

    /// <summary>Creates a runner whose factory observes cancellation while loading and initializing.</summary>
    public PrologTestRunner(Func<CancellationToken, PrologEngine> engineFactory)
    {
        ArgumentNullException.ThrowIfNull(engineFactory);
        _engineFactory = engineFactory;
    }

    /// <summary>How long discovery or one test, including initialization, may run.</summary>
    public TimeSpan Timeout { get; set; } = ConfiguredTimeout();

    /// <summary>Finds every test predicate, in the order the sources declare them.</summary>
    public IReadOnlyList<PrologTest> Discover() => RunTimed("Test discovery", Discover);

    private IReadOnlyList<PrologTest> Discover(CancellationToken cancellationToken)
    {
        using PrologEngine engine = _engineFactory(cancellationToken);
        engine.Machine.CancellationToken = cancellationToken;
        engine.Output = TextWriter.Null;
        engine.Input = TextReader.Null;

        List<PrologTest> tests = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        // Names are read back from the symbol table, which holds every atom the sources mentioned.
        for (var functorId = 0; functorId < engine.Program.Symbols.FunctorCount; functorId++)
        {
            Functor functor = engine.Program.Symbols.GetFunctor(functorId);
            if (functor.Arity != 0 || !engine.Program.IsDefined(functorId))
            {
                continue;
            }

            var name = engine.Program.Symbols.AtomName(functor.NameAtom);
            if (name.StartsWith(TestPrefix, StringComparison.Ordinal) && seen.Add(name))
            {
                tests.Add(new PrologTest(name, functorId));
            }
        }

        return tests;
    }

    /// <summary>Runs one test in a fresh engine and reports what happened.</summary>
    /// <remarks>
    /// Cancellation is cooperative. Bytecode and compiled dispatch observe it, but arbitrary host
    /// code or blocking I/O requires its own cancellation support. Process isolation is required
    /// for a hard termination guarantee. Only the worker disposes its engine and streams.
    /// </remarks>
    public PrologTestResult Run(PrologTest test)
    {
        try
        {
            return RunTimed(
                test.Name,
                cancellationToken =>
                {
                    using var output = new StringWriter();
                    using var error = new StringWriter();
                    using PrologEngine engine = _engineFactory(cancellationToken);
                    engine.Machine.CancellationToken = cancellationToken;
                    engine.Output = output;
                    engine.Error = error;
                    engine.Input = TextReader.Null;
                    return Execute(engine, test, output, error);
                }
            );
        }
        catch (TimeoutException error)
        {
            return PrologTestResult.Failed(error.Message, string.Empty, string.Empty);
        }
    }

    private T RunTimed<T>(string name, Func<CancellationToken, T> action)
    {
        using var cancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                result = action(token);
            }
            catch (Exception error)
            {
                // Marshal host failures back to the caller instead of terminating the process
                // with an unhandled exception on a background thread.
                failure = ExceptionDispatchInfo.Capture(error);
            }
        })
        {
            IsBackground = true,
            Name = $"DotProlog {name}",
        };

        worker.Start();
        if (worker.Join(Timeout))
        {
            failure?.Throw();
            return result;
        }

        cancellation.Cancel();
        var stopped = worker.Join(TimeSpan.FromSeconds(1));
        throw new TimeoutException(
            $"{name} did not complete within {Timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds; "
                + (
                    stopped
                        ? "the worker stopped after cancellation."
                        : "the worker did not stop after cancellation and was abandoned."
                )
        );
    }

    private static PrologTestResult Execute(PrologEngine engine, PrologTest test, StringWriter output, StringWriter error)
    {
        try
        {
            var functorId = engine.Program.Symbols.InternFunctor(test.Name, 0);
            RunResult result = engine.Machine.Solve(functorId);

            return result switch
            {
                RunResult.Success => PrologTestResult.Passed(output.ToString()),
                RunResult.Halted => PrologTestResult.Failed(
                    $"{test.Name} halted the test run.",
                    output.ToString(),
                    error.ToString()
                ),
                _ => PrologTestResult.Failed($"{test.Name} failed.", output.ToString(), error.ToString()),
            };
        }
        catch (PrologException thrown)
        {
            return PrologTestResult.Failed($"{test.Name} threw {thrown.Message}", output.ToString(), error.ToString());
        }
    }

    private static TimeSpan ConfiguredTimeout() =>
        int.TryParse(
            Environment.GetEnvironmentVariable(TimeoutVariable),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var seconds
        )
        && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : DefaultTimeout;
}
