using System.Globalization;
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

    private readonly Func<PrologEngine> _engineFactory;

    /// <summary>Creates a runner over the given Prolog sources.</summary>
    /// <param name="sources">Each source file's name and contents.</param>
    public PrologTestRunner(IReadOnlyList<(string Name, string Text)> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _engineFactory = () =>
        {
            // Muted while loading: directive output belongs to no single test, and during
            // discovery it would repeat once per scan.
            var engine = new PrologEngine
            {
                Output = TextWriter.Null,
                Error = TextWriter.Null,
                Input = TextReader.Null,
            };
            foreach ((string name, string text) in sources)
            {
                engine.ConsultOrThrow(text, name);
            }

            engine.RunPendingGoals();
            return engine;
        };
    }

    /// <summary>Creates a runner over a build-time-generated engine factory.</summary>
    public PrologTestRunner(Func<PrologEngine> engineFactory)
    {
        ArgumentNullException.ThrowIfNull(engineFactory);
        _engineFactory = engineFactory;
    }

    /// <summary>How long one test may run before it is reported as failed and abandoned.</summary>
    public TimeSpan Timeout { get; set; } = ConfiguredTimeout();

    /// <summary>Finds every test predicate, in the order the sources declare them.</summary>
    public IReadOnlyList<PrologTest> Discover()
    {
        PrologEngine engine = _engineFactory();
        engine.Output = TextWriter.Null;
        engine.Input = TextReader.Null;

        List<PrologTest> tests = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        // Names are read back from the symbol table, which holds every atom the sources mentioned.
        for (int functorId = 0; functorId < engine.Program.Symbols.FunctorCount; functorId++)
        {
            Functor functor = engine.Program.Symbols.GetFunctor(functorId);
            if (functor.Arity != 0 || !engine.Program.IsDefined(functorId))
            {
                continue;
            }

            string name = engine.Program.Symbols.AtomName(functor.NameAtom);
            if (name.StartsWith(TestPrefix, StringComparison.Ordinal) && seen.Add(name))
            {
                tests.Add(new PrologTest(name, functorId));
            }
        }

        return tests;
    }

    /// <summary>Runs one test in a fresh engine and reports what happened.</summary>
    /// <remarks>
    /// The goal runs on its own thread so a looping test can be reported as failed rather than
    /// hanging the run. The engine is not thread-safe and cannot be reclaimed mid-run, so on a
    /// timeout it is asked to halt and abandoned with its thread, which is a background thread
    /// precisely so an unstoppable loop cannot keep the process alive.
    /// </remarks>
    public PrologTestResult Run(PrologTest test)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        // Input is empty rather than the console: a test that reads would otherwise block the run.
        PrologEngine engine = _engineFactory();
        engine.Output = output;
        engine.Error = error;
        engine.Input = TextReader.Null;

        PrologTestResult? result = null;
        var worker = new Thread(() => result = Execute(engine, test, output, error))
        {
            IsBackground = true,
            Name = $"DotProlog test {test.Name}",
        };

        worker.Start();
        if (worker.Join(Timeout))
        {
            return result!;
        }

        try
        {
            engine.Machine.RequestHalt(1);
        }
        catch (IOException)
        {
            // Halting closes the machine's streams while the abandoned thread may still use them;
            // the halt is best effort, so a closing race is not this test's failure.
        }

        // The writers are still owned by the abandoned thread, so their contents are not read here.
        return PrologTestResult.Failed(
            $"{test.Name} did not complete within {Timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds and was abandoned.",
            string.Empty,
            string.Empty
        );
    }

    private static PrologTestResult Execute(PrologEngine engine, PrologTest test, StringWriter output, StringWriter error)
    {
        try
        {
            int functorId = engine.Program.Symbols.InternFunctor(test.Name, 0);
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
            out int seconds
        )
        && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : DefaultTimeout;
}
