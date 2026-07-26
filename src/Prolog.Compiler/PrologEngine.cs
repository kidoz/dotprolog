using Prolog.Runtime;
using Prolog.Syntax;

namespace Prolog.Compiler;

/// <summary>
/// Reads Prolog source, compiles it to bytecode, and runs it. This is the entry point both the
/// <c>dotnet prolog</c> tool and embedding hosts use.
/// </summary>
/// <remarks>
/// This is the runtime-consult path: source becomes bytecode executed by <see cref="Machine"/>. It
/// never emits CLR IL, so it stays valid inside a NativeAOT process. The build-time path that turns
/// Prolog into generated C# is a separate component.
/// </remarks>
public sealed class PrologEngine : IRuntimeCompiler
{
    private readonly List<int> _pendingDirectives = [];
    private readonly List<int> _pendingInitialization = [];

    /// <summary>Creates an engine with the core builtins registered and an empty program.</summary>
    public PrologEngine()
    {
        Program = new BytecodeProgram();
        CoreBuiltins.RegisterAll(Program);
        Machine = new Machine(Program);
        Program.RuntimeCompiler = this;

        LoadResult bootstrap = new ProgramLoader(Program, Machine).Load(
            ReadOrThrow(BootstrapLibrary.Source, "bootstrap"),
            "bootstrap"
        );

        if (!bootstrap.Success)
        {
            throw new PrologException($"The bootstrap library failed to compile: {string.Join("; ", bootstrap.Diagnostics)}");
        }
    }

    private static IReadOnlyList<SyntaxTerm> ReadOrThrow(string source, string fileName)
    {
        ParseResult parsed = TermReader.ReadProgram(source, fileName);
        if (!parsed.Success)
        {
            throw new PrologException($"The {fileName} library failed to parse: {string.Join("; ", parsed.Diagnostics)}");
        }

        return parsed.Clauses;
    }

    /// <summary>The program this engine loads into.</summary>
    public BytecodeProgram Program { get; }

    /// <summary>The machine that executes the program.</summary>
    public Machine Machine { get; }

    /// <summary>Where the program's output goes.</summary>
    public TextWriter Output
    {
        get => Machine.Output;
        set => Machine.Output = value;
    }

    /// <summary>Reads and compiles the Prolog file at <paramref name="path"/>.</summary>
    public LoadResult ConsultFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return ConsultText(File.ReadAllText(path), path);
    }

    /// <summary>Reads and compiles <paramref name="text"/> as a Prolog source unit.</summary>
    /// <param name="text">Prolog source.</param>
    /// <param name="fileName">File name used in diagnostics, when known.</param>
    public LoadResult ConsultText(string text, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        ParseResult parsed = TermReader.ReadProgram(text, fileName);
        if (!parsed.Success)
        {
            return new LoadResult(parsed.Diagnostics, [], []);
        }

        LoadResult loaded = new ProgramLoader(Program, Machine).Load(parsed.Clauses, fileName);
        List<Diagnostic> diagnostics = [.. parsed.Diagnostics, .. loaded.Diagnostics];

        if (loaded.Success)
        {
            _pendingDirectives.AddRange(loaded.DirectiveAddresses);
            _pendingInitialization.AddRange(loaded.InitializationAddresses);
        }

        return new LoadResult(diagnostics, loaded.DirectiveAddresses, loaded.InitializationAddresses);
    }

    /// <summary>
    /// Runs the directives and then the <c>initialization/1</c> goals collected by earlier consults,
    /// clearing the queue. A goal that merely fails produces a warning and does not stop the rest;
    /// <c>halt/0</c> and <c>halt/1</c> stop immediately.
    /// </summary>
    public RunResult RunPendingGoals()
    {
        RunResult result = RunQueue(_pendingDirectives, "directive");
        if (result == RunResult.Halted)
        {
            _pendingInitialization.Clear();
            return result;
        }

        return RunQueue(_pendingInitialization, "initialization goal");
    }

    /// <summary>
    /// Compiles <paramref name="goalText"/> into the program and returns the address to run it from,
    /// or -1 if it could not be compiled. The block stays in the program, so a goal that will be run
    /// repeatedly should be compiled once.
    /// </summary>
    /// <param name="goalText">A goal in Prolog syntax, with or without a trailing full stop.</param>
    /// <param name="diagnostics">Diagnostics raised while reading or compiling the goal.</param>
    public int CompileGoal(string goalText, out IReadOnlyList<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(goalText);

        ParseResult parsed = TermReader.ReadTerm(goalText);
        if (!parsed.Success || parsed.Clauses.Count == 0)
        {
            diagnostics = parsed.Diagnostics;
            return -1;
        }

        List<Diagnostic> compileDiagnostics = [];
        var compiler = new ClauseCompiler(Program, new ConstantPool(Program), compileDiagnostics, null);
        int address = compiler.Compile(new AtomTerm("$goal", parsed.Clauses[0].Span), parsed.Clauses[0]);
        diagnostics = compileDiagnostics;
        return address;
    }

    /// <summary>
    /// Compiles <paramref name="goalText"/> into a query a host can ask for answers.
    /// </summary>
    /// <remarks>
    /// The goal is wrapped as <c>'$bindings'(v(V1, …, Vn)), Goal</c>, which puts every variable of the
    /// goal into one term built before the goal's first choice point. That term therefore survives
    /// every backtrack the goal makes, and each answer can be read straight out of it.
    /// </remarks>
    /// <param name="goalText">A goal in Prolog syntax, with or without a trailing full stop.</param>
    /// <exception cref="PrologException">The goal could not be read or compiled.</exception>
    public PrologQuery Query(string goalText)
    {
        ArgumentNullException.ThrowIfNull(goalText);

        ParseResult parsed = TermReader.ReadTerm(goalText);
        if (!parsed.Success || parsed.Clauses.Count == 0)
        {
            throw new PrologException($"The goal did not parse: {string.Join("; ", parsed.Diagnostics)}");
        }

        SyntaxTerm goal = parsed.Clauses[0];
        string[] names = CollectVariableNames(goal);
        SyntaxTerm body = goal;

        if (names.Length > 0)
        {
            var holder = new CompoundTerm(
                "v",
                [.. names.Select(name => (SyntaxTerm)new VariableTerm(name, goal.Span))],
                goal.Span
            );

            body = new CompoundTerm(",", [new CompoundTerm("$bindings", [holder], goal.Span), goal], goal.Span);
        }

        List<Diagnostic> diagnostics = [];
        var compiler = new ClauseCompiler(Program, new ConstantPool(Program), diagnostics, null);
        int address = compiler.Compile(new AtomTerm("$query", goal.Span), body);

        return address < 0
            ? throw new PrologException($"The goal did not compile: {string.Join("; ", diagnostics)}")
            : new PrologQuery(this, address, names);
    }

    /// <summary>Collects the goal's named variables, in first-occurrence order.</summary>
    private static string[] CollectVariableNames(SyntaxTerm goal)
    {
        List<string> names = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<SyntaxTerm> pending = [goal];

        while (pending.Count > 0)
        {
            SyntaxTerm term = pending[^1];
            pending.RemoveAt(pending.Count - 1);

            switch (term)
            {
                // '_' is deliberately not reported: each occurrence is a different variable.
                case VariableTerm { IsAnonymous: false } variable when seen.Add(variable.Name):
                    names.Add(variable.Name);
                    break;

                case CompoundTerm compound:
                    for (int i = compound.Arity - 1; i >= 0; i--)
                    {
                        pending.Add(compound.Arguments[i]);
                    }

                    break;

                default:
                    break;
            }
        }

        return [.. names];
    }

    /// <summary>Proves <paramref name="goalText"/>, read as a single term, and reports the outcome.</summary>
    /// <param name="goalText">A goal in Prolog syntax, with or without a trailing full stop.</param>
    /// <param name="diagnostics">Diagnostics raised while reading or compiling the goal.</param>
    public RunResult RunGoal(string goalText, out IReadOnlyList<Diagnostic> diagnostics)
    {
        int address = CompileGoal(goalText, out diagnostics);
        return address < 0 ? RunResult.Failure : Machine.Run(address);
    }

    /// <inheritdoc />
    /// <remarks>
    /// This is the <c>assertz/1</c> path. It compiles into the same program the goal is running from,
    /// which is only safe because the program is append-only and the dispatch loop refreshes its
    /// cached arrays after every builtin.
    /// </remarks>
    public int CompileClause(Machine machine, Cell clause, out int functorId)
    {
        ArgumentNullException.ThrowIfNull(machine);

        SyntaxTerm term = TermReifier.ToSyntax(machine, clause);
        SyntaxTerm head = term;
        SyntaxTerm? body = null;

        if (term is CompoundTerm { Name: ":-", Arity: 2 } rule)
        {
            head = rule.Arguments[0];
            body = rule.Arguments[1];
        }

        functorId = head switch
        {
            AtomTerm atom => Program.Symbols.InternFunctor(atom.Name, 0),
            CompoundTerm compound => Program.Symbols.InternFunctor(compound.Name, compound.Arity),
            _ => throw new PrologException("type_error(callable, _)"),
        };

        List<Diagnostic> diagnostics = [];
        var compiler = new ClauseCompiler(Program, new ConstantPool(Program), diagnostics, null);
        int address = compiler.Compile(head, body);

        return address < 0
            ? throw new PrologException($"The asserted clause did not compile: {string.Join("; ", diagnostics)}")
            : address;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Directives in a file consulted at run time cannot execute immediately, because the machine is
    /// already running a goal and is not re-entrant. They are queued and run by the next call to
    /// <see cref="RunPendingGoals"/>.
    /// </remarks>
    public void ConsultFile(Machine machine, string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            throw new PrologException($"existence_error(source_sink, {path})");
        }

        LoadResult loaded = ConsultText(File.ReadAllText(path), path);
        if (!loaded.Success)
        {
            throw new PrologException($"Consulting {path} failed: {string.Join("; ", loaded.Diagnostics)}");
        }
    }

    private RunResult RunQueue(List<int> queue, string description)
    {
        foreach (int address in queue)
        {
            RunResult result = Machine.Run(address);
            if (result == RunResult.Halted)
            {
                queue.Clear();
                return result;
            }

            if (result == RunResult.Failure)
            {
                Output.Write($"Warning: {description} failed.\n");
            }
        }

        queue.Clear();
        return RunResult.Success;
    }
}
