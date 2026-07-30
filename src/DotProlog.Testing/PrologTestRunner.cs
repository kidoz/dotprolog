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

    private readonly Func<PrologEngine> _engineFactory;

    /// <summary>Creates a runner over the given Prolog sources.</summary>
    /// <param name="sources">Each source file's name and contents.</param>
    public PrologTestRunner(IReadOnlyList<(string Name, string Text)> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _engineFactory = () =>
        {
            var engine = new PrologEngine();
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
    public PrologTestResult Run(PrologTest test)
    {
        var output = new StringWriter();
        // Input is empty rather than the console: a test that reads would otherwise block the run.
        PrologEngine engine = _engineFactory();
        engine.Output = output;
        engine.Input = TextReader.Null;

        try
        {
            int functorId = engine.Program.Symbols.InternFunctor(test.Name, 0);
            RunResult result = engine.Machine.Solve(functorId);

            return result switch
            {
                RunResult.Success => PrologTestResult.Passed(output.ToString()),
                RunResult.Halted => PrologTestResult.Failed($"{test.Name} halted the test run.", output.ToString()),
                _ => PrologTestResult.Failed($"{test.Name} failed.", output.ToString()),
            };
        }
        catch (PrologException error)
        {
            return PrologTestResult.Failed($"{test.Name} threw {error.Message}", output.ToString());
        }
    }
}
