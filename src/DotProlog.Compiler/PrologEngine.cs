using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

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
    private readonly ModuleTable _modules = new();
    private readonly List<int> _pendingDirectives = [];
    private readonly List<int> _pendingInitialization = [];

    /// <summary>Creates an engine with the core builtins registered and an empty program.</summary>
    public PrologEngine()
    {
        Program = new BytecodeProgram();
        CoreBuiltins.RegisterAll(Program);
        Machine = new Machine(Program);
        Program.RuntimeCompiler = this;

        LoadLibrary(BootstrapLibrary.Source, "bootstrap");
        LoadLibrary(StandardLibrary.Source, "library");
    }

    /// <summary>Compiles one of the built-in libraries, which must not fail.</summary>
    private void LoadLibrary(string source, string name)
    {
        LoadResult loaded = new ProgramLoader(Program, Machine, _modules).Load(ReadOrThrow(source, name), name);

        if (!loaded.Success)
        {
            throw new PrologException($"The {name} library failed to compile: {string.Join("; ", loaded.Diagnostics)}");
        }
    }

    private IReadOnlyList<SyntaxTerm> ReadOrThrow(string source, string fileName)
    {
        ParseResult parsed = TermReader.ReadProgram(source, fileName, Program.Operators);
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

    /// <summary>Where the program's <c>read/1</c> and friends take their input from.</summary>
    public TextReader Input
    {
        get => Machine.Input;
        set => Machine.Input = value;
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

        ParseResult parsed = TermReader.ReadProgram(text, fileName, Program.Operators);
        if (!parsed.Success)
        {
            return new LoadResult(parsed.Diagnostics, [], []);
        }

        var loader = new ProgramLoader(Program, Machine, _modules);
        LoadResult loaded = loader.Load(parsed.Clauses, fileName);
        List<Diagnostic> diagnostics = [.. parsed.Diagnostics, .. loaded.Diagnostics];

        // Which module a file declared is what use_module/1 needs to know when it is imported later.
        if (fileName is not null && File.Exists(fileName))
        {
            _modules.RecordLoad(Path.GetFullPath(fileName), loader.Module);
        }

        if (loaded.Success)
        {
            _pendingDirectives.AddRange(loaded.DirectiveAddresses);
            _pendingInitialization.AddRange(loaded.InitializationAddresses);
        }

        return new LoadResult(diagnostics, loaded.DirectiveAddresses, loaded.InitializationAddresses);
    }

    /// <summary>
    /// Consults <paramref name="text"/> and throws if it does not compile, with the diagnostics in the
    /// message.
    /// </summary>
    /// <remarks>
    /// This exists for generated facades: it keeps their loading path to one line and means the
    /// generated assembly needs only <c>DotProlog.Runtime</c> and <c>DotProlog.Compiler</c>, never
    /// <c>DotProlog.Syntax</c> for the diagnostic type.
    /// </remarks>
    /// <param name="text">Prolog source.</param>
    /// <param name="fileName">File name used in diagnostics.</param>
    /// <exception cref="PrologException">The source did not compile.</exception>
    public void ConsultOrThrow(string text, string fileName)
    {
        LoadResult loaded = ConsultText(text, fileName);
        if (!loaded.Success)
        {
            throw new PrologException($"{fileName} did not compile: {string.Join("; ", loaded.Diagnostics)}");
        }
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

        ParseResult parsed = TermReader.ReadTerm(goalText, operators: Program.Operators);
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

        ParseResult parsed = TermReader.ReadTerm(goalText, operators: Program.Operators);
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
    /// This is the meta-called-control path. The anonymous clause receives the original live
    /// variables as arguments, so compiling the body in place preserves both aliasing and ISO cut
    /// scope without adding control state to the runtime machine.
    /// </remarks>
    public int CompileControlGoal(Machine machine, Cell goal, Span<Cell> arguments, out int argumentCount)
    {
        ArgumentNullException.ThrowIfNull(machine);

        var variables = new Dictionary<string, Cell>(StringComparer.Ordinal);
        SyntaxTerm body = TermReifier.ToSyntax(machine, goal, variables);
        string[] names = CollectVariableNames(body);

        if (names.Length >= Machine.ArgumentRegisterCount || names.Length > arguments.Length)
        {
            throw PrologErrors.Representation(machine, "max_arity");
        }

        var headArguments = new SyntaxTerm[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i];
            headArguments[i] = new VariableTerm(name, SourceSpan.None);
            arguments[i] = variables[name];
        }

        SyntaxTerm head =
            headArguments.Length == 0
                ? new AtomTerm("$meta_control", SourceSpan.None)
                : new CompoundTerm("$meta_control", headArguments, SourceSpan.None);

        // Keep the anonymous frame alive until the whole meta-call returns. A choice point created
        // by an earlier subgoal restores this frame on redo; tail-executing the last subgoal would
        // otherwise let that subgoal reuse stack space still named by the choice point.
        var completion = new CompoundTerm("call", [new AtomTerm("true", SourceSpan.None)], SourceSpan.None);
        body = new CompoundTerm(",", [body, completion], SourceSpan.None);

        List<Diagnostic> diagnostics = [];
        var compiler = new ClauseCompiler(Program, new ConstantPool(Program), diagnostics, null);
        int address = compiler.Compile(head, body);
        argumentCount = names.Length;

        return address < 0
            ? throw new PrologException($"The meta-called control term did not compile: {string.Join("; ", diagnostics)}")
            : address;
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

        // The clause is inspected as cells before it is reified, because an error raised here has
        // to name the offending part of the term the program passed, and catch/3 can only unify
        // against a term the machine built.
        Cell whole = machine.Dereference(clause);
        if (whole.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        Cell headCell = whole;
        Cell bodyCell = default;
        bool hasBody = false;
        int rule2 = machine.Symbols.InternFunctor(":-", 2);

        if (whole.Tag == CellTag.Structure && machine.HeapAt(whole.Index).Index == rule2)
        {
            headCell = machine.Dereference(machine.HeapAt(whole.Index + 1));
            bodyCell = machine.Dereference(machine.HeapAt(whole.Index + 2));
            hasBody = true;
        }

        if (headCell.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        if (headCell.Tag is not (CellTag.Atom or CellTag.Structure))
        {
            throw PrologErrors.Type(machine, "callable", headCell);
        }

        SyntaxTerm term = TermReifier.ToSyntax(machine, clause);
        SyntaxTerm head = term;
        SyntaxTerm? body = null;
        List<Diagnostic> diagnostics = [];

        // A grammar rule asserted at run time is translated exactly as one read from a file, so
        // assertz((greeting --> [hello])) defines greeting//0 rather than a clause of -->/2.
        if (term is CompoundTerm { Name: "-->", Arity: 2 } grammarRule)
        {
            if (!DcgTranslator.TryTranslate(grammarRule, diagnostics, null, out head, out SyntaxTerm translated))
            {
                throw PrologErrors.Type(machine, "callable", whole);
            }

            body = translated;
        }
        else if (term is CompoundTerm { Name: ":-", Arity: 2 } rule)
        {
            head = rule.Arguments[0];
            body = rule.Arguments[1];
        }

        functorId = head switch
        {
            AtomTerm atom => Program.Symbols.InternFunctor(atom.Name, 0),
            CompoundTerm compound => Program.Symbols.InternFunctor(compound.Name, compound.Arity),
            _ => throw PrologErrors.Type(machine, "callable", headCell),
        };

        var compiler = new ClauseCompiler(Program, new ConstantPool(Program), diagnostics, null);
        int address = compiler.Compile(head, body);

        // ISO 8.9.1.3: a body that is not callable is type_error(callable, Body). Anything else the
        // compiler rejects is reported the same way, since the body is what could not be compiled.
        return address < 0 ? throw PrologErrors.Type(machine, "callable", hasBody ? bodyCell : whole) : address;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Directives in a file consulted at run time cannot execute immediately, because the machine is
    /// already running a goal and is not re-entrant. They are queued and run by the next call to
    /// <see cref="RunPendingGoals"/>.
    /// </remarks>
    public void ConsultFile(Machine machine, string path)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            throw ExistenceError(machine, "source_sink", Cell.Atom(machine.Symbols.InternAtom(path)));
        }

        LoadResult loaded = ConsultText(File.ReadAllText(path), path);
        if (!loaded.Success)
        {
            throw SyntaxError(machine, $"{path}: {string.Join("; ", loaded.Diagnostics)}");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Text is pulled a line at a time until the lexer finds a clause terminator, so a term can be
    /// read from a console as soon as it is complete rather than after the input ends. Whatever
    /// follows the terminator stays in the buffer, which belongs to the stream.
    /// </remarks>
    public bool TryReadTerm(Machine machine, TextReader input, ref string buffer, out Cell term, out Cell variableNames)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(input);

        term = default;
        variableNames = Cell.Atom(machine.Symbols.EmptyList);

        int end = ClauseScanner.FindClauseEnd(buffer);
        while (end < 0)
        {
            string? line = input.ReadLine();
            if (line is null)
            {
                // What is left at end of input is either nothing, which is end_of_file, or a clause
                // missing its terminator. Reading the incomplete text as if it were whole would
                // quietly return a prefix of what the file says.
                bool blank = ClauseScanner.IsBlank(buffer);
                buffer = string.Empty;

                return blank ? false : throw SyntaxError(machine, "unexpected_end_of_file");
            }

            buffer += line + "\n";
            end = ClauseScanner.FindClauseEnd(buffer);
        }

        string text = buffer[..end];
        buffer = buffer[end..];

        ParseResult parsed = TermReader.ReadTerm(text, operators: Program.Operators);
        if (!parsed.Success || parsed.Clauses.Count == 0)
        {
            throw SyntaxError(machine, parsed.Diagnostics.Count > 0 ? parsed.Diagnostics[0].Id : "cannot_start_term");
        }

        Dictionary<string, Cell> variables = [];
        term = TermReifier.ToHeap(machine, TermNormalizer.Normalize(parsed.Clauses[0]), variables);

        // Only named variables are reported, and in the order the reader met them, so that
        // variable_names/1 reads the way the source does.
        List<Cell> pairs = [];
        int equals = machine.Symbols.InternFunctor("=", 2);
        foreach (string name in CollectVariableNames(parsed.Clauses[0]))
        {
            if (variables.TryGetValue(name, out Cell variable))
            {
                pairs.Add(machine.CreateStructure(equals, [Cell.Atom(machine.Symbols.InternAtom(name)), variable]));
            }
        }

        variableNames = machine.CreateList(pairs.ToArray(), Cell.Atom(machine.Symbols.EmptyList));
        return true;
    }

    /// <summary>
    /// Builds a catchable <c>error(syntax_error(What), _)</c>, so that a program reading terms from
    /// a file it does not control can handle a bad one rather than being stopped by it.
    /// </summary>
    private static PrologException SyntaxError(Machine machine, string what)
    {
        Cell formal = machine.CreateStructure(
            machine.Symbols.InternFunctor("syntax_error", 1),
            [Cell.Atom(machine.Symbols.InternAtom(what))]
        );

        Cell error = machine.CreateStructure(machine.Symbols.InternFunctor("error", 2), [formal, machine.CreateVariable()]);
        return machine.CreateBall(error, $"syntax_error({what})");
    }

    /// <summary>Builds a catchable <c>error(existence_error(Kind, Culprit), _)</c>.</summary>
    private static PrologException ExistenceError(Machine machine, string kind, Cell culprit)
    {
        Cell formal = machine.CreateStructure(
            machine.Symbols.InternFunctor("existence_error", 2),
            [Cell.Atom(machine.Symbols.InternAtom(kind)), culprit]
        );

        Cell error = machine.CreateStructure(machine.Symbols.InternFunctor("error", 2), [formal, machine.CreateVariable()]);
        return machine.CreateBall(error, $"existence_error({kind}, {TermWriter.ToDisplayString(machine, culprit)})");
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
