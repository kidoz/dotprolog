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
    private const int ControlGoalCacheLimit = 1024;

    private readonly ModuleTable _modules = new();
    private readonly List<int> _pendingDirectives = [];
    private readonly List<int> _pendingInitialization = [];
    private readonly HashSet<string> _loadedSourceFiles = new(PathComparer);
    private readonly HashSet<string> _loadingSourceFiles = new(PathComparer);
    private readonly Dictionary<string, (int Address, int ArgumentCount)> _controlGoals = new(StringComparer.Ordinal);
    private bool _preparationHalted;

    /// <summary>Creates an extended-mode engine with the core builtins registered and an empty program.</summary>
    public PrologEngine()
        : this(PrologLanguageMode.Extended) { }

    /// <summary>Creates an engine using <paramref name="languageMode"/>.</summary>
    public PrologEngine(PrologLanguageMode languageMode)
    {
        Program = new BytecodeProgram(languageMode);
        CoreBuiltins.RegisterAll(Program);
        Machine = new Machine(Program);
        Program.RuntimeCompiler = this;

        // The bundled libraries are processor implementation, not user source, so they are read under
        // the ISO initial value whatever mode the host chose. Modern mode cannot reinterpret them.
        Program.Flags.DoubleQuotes = DoubleQuotesMode.Codes;
        LoadLibrary(BootstrapLibrary.Source, "bootstrap");
        LoadLibrary(StandardLibrary.Source, "library");
        Program.Flags.DoubleQuotes = Program.InitialDoubleQuotes;
    }

    /// <summary>Compiles one of the built-in libraries, which must not fail.</summary>
    private void LoadLibrary(string source, string name)
    {
        LoadResult loaded = new ProgramLoader(Program, Machine, _modules, userPredicates: false).Load(
            ReadOrThrow(source, name),
            name
        );

        if (!loaded.Success)
        {
            throw new PrologException($"The {name} library failed to compile: {string.Join("; ", loaded.Diagnostics)}");
        }
    }

    private IReadOnlyList<SyntaxTerm> ReadOrThrow(string source, string fileName)
    {
        ParseResult parsed = TermReader.ReadProgram(
            source,
            fileName,
            Program.Operators,
            Program.CharacterConversions,
            Program.Flags
        );
        if (!parsed.Success)
        {
            throw new PrologException($"The {fileName} library failed to parse: {string.Join("; ", parsed.Diagnostics)}");
        }

        return parsed.Clauses;
    }

    private ParseResult ReadTerm(string text) =>
        TermReader.ReadTerm(
            text,
            operators: Program.Operators,
            characterConversions: Program.CharacterConversions,
            flags: Program.Flags
        );

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

    /// <summary>Where warnings and diagnostics raised while running the program go.</summary>
    public TextWriter Error
    {
        get => Machine.Error;
        set => Machine.Error = value;
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
        string absolute = Path.GetFullPath(path);
        LoadResult loaded = ConsultText(File.ReadAllText(absolute), absolute);
        if (loaded.Success)
        {
            _loadedSourceFiles.Add(absolute);
        }

        return loaded;
    }

    /// <summary>Reads and compiles <paramref name="text"/> as a Prolog source unit.</summary>
    /// <param name="text">Prolog source.</param>
    /// <param name="fileName">File name used in diagnostics, when known.</param>
    public LoadResult ConsultText(string text, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        ParseResult parsed = ReadProgramWithIncludes(text, fileName);
        if (!parsed.Success)
        {
            return new LoadResult(parsed.Diagnostics, [], []);
        }

        var loader = new ProgramLoader(Program, Machine, _modules);
        LoadResult loaded = loader.Load(parsed.Clauses, fileName, Machine.IsRunning ? null : ExecutePreparationDirective);
        List<Diagnostic> diagnostics = [.. parsed.Diagnostics, .. loaded.Diagnostics];

        // Which module a file declared is what use_module/1 needs to know when it is imported later.
        if (loaded.Success && fileName is not null && File.Exists(fileName))
        {
            string absolute = Path.GetFullPath(fileName);
            _modules.RecordLoad(absolute, loader.Module);
            _loadedSourceFiles.Add(absolute);
        }

        if (loaded.Success)
        {
            _pendingDirectives.AddRange(loaded.DirectiveAddresses);
            _pendingInitialization.AddRange(loaded.InitializationAddresses);
        }

        return new LoadResult(diagnostics, loaded.DirectiveAddresses, loaded.InitializationAddresses);
    }

    /// <summary>
    /// Compiles a build-time source while reporting each executable directive at its publication
    /// point. The generated-C# backend uses the snapshots to reproduce source preparation at
    /// application startup.
    /// </summary>
    internal LoadResult CompileForGeneratedCode(string text, string? fileName, Action<int> directiveObserver)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(directiveObserver);

        ParseResult parsed = ReadProgramWithIncludes(text, fileName);
        if (!parsed.Success)
        {
            return new LoadResult(parsed.Diagnostics, [], []);
        }

        var loader = new ProgramLoader(Program, Machine, _modules);
        LoadResult loaded = loader.Load(
            parsed.Clauses,
            fileName,
            address =>
            {
                directiveObserver(address);
                return RunResult.Success;
            }
        );
        List<Diagnostic> diagnostics = [.. parsed.Diagnostics, .. loaded.Diagnostics];
        return new LoadResult(diagnostics, loaded.DirectiveAddresses, loaded.InitializationAddresses);
    }

    /// <summary>
    /// Reads a source unit while expanding ISO <c>include/1</c> declarations at their source
    /// position. All nested readers share the program's reader state.
    /// </summary>
    private ParseResult ReadProgramWithIncludes(string text, string? fileName)
    {
        HashSet<string> active = new(PathComparer);
        string? rootPath = ExistingFullPath(fileName);
        if (rootPath is not null)
        {
            active.Add(rootPath);
        }

        return ReadSource(text, fileName);

        ParseResult ReadSource(string source, string? sourceName) =>
            TermReader.ReadProgram(
                source,
                sourceName,
                Program.Operators,
                Program.CharacterConversions,
                Program.Flags,
                clause => ExpandInclude(clause, sourceName)
            );

        ParseResult? ExpandInclude(SyntaxTerm clause, string? includingFile)
        {
            if (
                clause is not CompoundTerm { Name: ":-", Arity: 1 } directive
                || directive.Arguments[0] is not CompoundTerm { Name: "include", Arity: 1 } include
            )
            {
                return null;
            }

            if (include.Arguments[0] is not AtomTerm file)
            {
                return IncludeError(
                    CompilerDiagnosticIds.InvalidIncludeDeclaration,
                    "include/1 needs an atom naming a source file.",
                    include.Arguments[0].Span,
                    includingFile
                );
            }

            string? path = ResolveSourcePath(file.Name, includingFile);
            if (path is null)
            {
                return IncludeError(
                    CompilerDiagnosticIds.IncludeNotFound,
                    $"No file for include({file.Name}).",
                    file.Span,
                    includingFile
                );
            }

            if (!active.Add(path))
            {
                return IncludeError(
                    CompilerDiagnosticIds.IncludeCycle,
                    $"Recursive include of '{path}'.",
                    file.Span,
                    includingFile
                );
            }

            try
            {
                return ReadSource(File.ReadAllText(path), path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return IncludeError(
                    CompilerDiagnosticIds.IncludeNotFound,
                    $"Could not read included file '{path}': {exception.Message}",
                    file.Span,
                    includingFile
                );
            }
            finally
            {
                active.Remove(path);
            }
        }
    }

    private static ParseResult IncludeError(string id, string message, SourceSpan span, string? fileName) =>
        new([], [new Diagnostic(id, DiagnosticSeverity.Error, message, span, fileName)]);

    private static string? ExistingFullPath(string? fileName) =>
        fileName is not null && File.Exists(fileName) ? Path.GetFullPath(fileName) : null;

    private static string? ResolveSourcePath(string name, string? includingFile)
    {
        string directory = includingFile is null
            ? Directory.GetCurrentDirectory()
            : (Path.GetDirectoryName(Path.GetFullPath(includingFile)) ?? ".");

        foreach (string candidate in (string[])[name, name + ".pl"])
        {
            string absolute = Path.IsPathRooted(candidate) ? candidate : Path.Combine(directory, candidate);
            if (File.Exists(absolute))
            {
                return Path.GetFullPath(absolute);
            }
        }

        return null;
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

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
        if (_preparationHalted)
        {
            _preparationHalted = false;
            _pendingDirectives.Clear();
            _pendingInitialization.Clear();
            return RunResult.Halted;
        }

        RunResult result = RunQueue(_pendingDirectives, "directive");
        if (result == RunResult.Halted)
        {
            _pendingInitialization.Clear();
            return result;
        }

        return RunQueue(_pendingInitialization, "initialization goal");
    }

    private RunResult ExecutePreparationDirective(int address)
    {
        RunResult result = Machine.Run(address);
        if (result == RunResult.Failure)
        {
            Output.Write("Warning: directive failed.\n");
        }
        else if (result == RunResult.Halted)
        {
            _preparationHalted = true;
        }

        return result;
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

        ParseResult parsed = ReadTerm(goalText);
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

        ParseResult parsed = ReadTerm(goalText);
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
        var compiler = new ClauseCompiler(Program, new ConstantPool(Program), diagnostics, null, allowQueryBindings: true);
        int address = compiler.Compile(new AtomTerm("$query", goal.Span), body);

        return address < 0
            ? throw new PrologException($"The goal did not compile: {string.Join("; ", diagnostics)}")
            : new PrologQuery(this, address, names);
    }

    /// <summary>Collects the goal's named variables, in first-occurrence order.</summary>
    private static string[] CollectVariableNames(SyntaxTerm goal)
    {
        return [.. CollectNamedVariables(goal).Select(variable => variable.Name)];
    }

    /// <summary>Collects named variables and occurrence counts in first-occurrence order.</summary>
    private static NamedVariable[] CollectNamedVariables(SyntaxTerm goal)
    {
        List<string> order = [];
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        List<SyntaxTerm> pending = [goal];

        while (pending.Count > 0)
        {
            SyntaxTerm term = pending[^1];
            pending.RemoveAt(pending.Count - 1);

            switch (term)
            {
                // '_' is deliberately not reported: each occurrence is a different variable.
                case VariableTerm { IsAnonymous: false } variable:
                    if (counts.TryGetValue(variable.Name, out int count))
                    {
                        counts[variable.Name] = count + 1;
                    }
                    else
                    {
                        counts[variable.Name] = 1;
                        order.Add(variable.Name);
                    }

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

        return [.. order.Select(name => new NamedVariable(name, counts[name]))];
    }

    private readonly record struct NamedVariable(string Name, int Count);

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
    /// scope without adding control state to the runtime machine. The compiled code depends only on
    /// the goal's shape with variables numbered by first occurrence, so one clause per shape is
    /// cached: the program is append-only, and recompiling the same shape on every meta-call would
    /// grow it without bound.
    /// </remarks>
    public int CompileControlGoal(Machine machine, Cell goal, Span<Cell> arguments, out int argumentCount)
    {
        ArgumentNullException.ThrowIfNull(machine);

        string key = ControlGoalKey(machine, goal, out List<Cell> goalVariables);

        if (_controlGoals.TryGetValue(key, out (int Address, int ArgumentCount) cached))
        {
            for (int i = 0; i < cached.ArgumentCount; i++)
            {
                arguments[i] = goalVariables[i];
            }

            argumentCount = cached.ArgumentCount;
            return cached.Address;
        }

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

        List<Diagnostic> diagnostics = [];
        var compiler = new ClauseCompiler(Program, new ConstantPool(Program), diagnostics, null);
        int address = compiler.Compile(head, body);
        argumentCount = names.Length;

        if (address < 0)
        {
            throw new PrologException($"The meta-called control term did not compile: {string.Join("; ", diagnostics)}");
        }

        if (_controlGoals.Count < ControlGoalCacheLimit)
        {
            _controlGoals[key] = (address, names.Length);
        }

        return address;
    }

    /// <summary>
    /// Builds the cache key for a meta-called control term — its structure with constants by
    /// interned identity and variables numbered by first occurrence — and collects those variables
    /// in the same order <see cref="CollectVariableNames"/> reaches them in the reified goal.
    /// </summary>
    private static string ControlGoalKey(Machine machine, Cell goal, out List<Cell> variables)
    {
        var key = new System.Text.StringBuilder();
        Dictionary<int, int> ordinals = [];
        HashSet<int> active = [];
        variables = [];
        List<(Cell Cell, bool Leaving)> work = [(goal, false)];

        while (work.Count > 0)
        {
            (Cell source, bool leaving) = work[^1];
            work.RemoveAt(work.Count - 1);

            if (leaving)
            {
                active.Remove(source.Index);
                key.Append(')');
                continue;
            }

            Cell cell = machine.Dereference(source);
            switch (cell.Tag)
            {
                case CellTag.Reference:
                    if (!ordinals.TryGetValue(cell.Index, out int ordinal))
                    {
                        ordinal = variables.Count;
                        ordinals[cell.Index] = ordinal;
                        variables.Add(cell);
                    }

                    key.Append('V').Append(ordinal).Append(',');
                    break;

                case CellTag.Atom:
                    key.Append('a').Append(cell.Index).Append(',');
                    break;

                case CellTag.Integer:
                    key.Append('i').Append(cell.Integer).Append(',');
                    break;

                case CellTag.Float:
                    key.Append('f').Append(cell.Index).Append(',');
                    break;

                case CellTag.Structure:
                {
                    // A rational control term cannot be reified into finite syntax; reject it with
                    // a catchable error rather than looping here or overflowing in the reifier.
                    if (!active.Add(cell.Index))
                    {
                        throw PrologErrors.Representation(machine, "cyclic_term");
                    }

                    int functorId = machine.HeapAt(cell.Index).Index;
                    key.Append('s').Append(functorId).Append('(');
                    work.Add((cell, true));
                    for (int i = machine.Symbols.ArityOf(functorId); i >= 1; i--)
                    {
                        work.Add((machine.HeapAt(cell.Index + i), false));
                    }

                    break;
                }

                default:
                    key.Append(cell.ToString()).Append(',');
                    break;
            }
        }

        return key.ToString();
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

        if (hasBody && bodyCell.Tag is not (CellTag.Reference or CellTag.Atom or CellTag.Structure))
        {
            throw PrologErrors.Type(machine, "callable", bodyCell);
        }

        SyntaxTerm term = TermReifier.ToSyntax(machine, clause);
        SyntaxTerm head = term;
        SyntaxTerm? body = null;
        List<Diagnostic> diagnostics = [];

        // A grammar rule asserted at run time is translated exactly as one read from a file, so
        // assertz((greeting --> [hello])) defines greeting//0 rather than a clause of -->/2.
        if (term is CompoundTerm { Name: "-->", Arity: 2 } grammarRule)
        {
            if (
                !DcgTranslator.TryTranslate(
                    grammarRule,
                    diagnostics,
                    null,
                    Program.LanguageMode,
                    out head,
                    out SyntaxTerm translated
                )
            )
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

        string? resolved = ResolveSourcePath(path, null);
        if (resolved is null)
        {
            throw ExistenceError(machine, "source_sink", Cell.Atom(machine.Symbols.InternAtom(path)));
        }

        LoadResult loaded = ConsultFile(resolved);
        if (!loaded.Success)
        {
            throw SyntaxError(machine, $"{resolved}: {string.Join("; ", loaded.Diagnostics)}");
        }
    }

    /// <inheritdoc />
    public void EnsureLoadedFile(Machine machine, string path)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(path);

        string? resolved = ResolveSourcePath(path, null);
        if (resolved is null)
        {
            throw ExistenceError(machine, "source_sink", Cell.Atom(machine.Symbols.InternAtom(path)));
        }

        if (_loadedSourceFiles.Contains(resolved) || !_loadingSourceFiles.Add(resolved))
        {
            return;
        }

        try
        {
            LoadResult loaded = ConsultFile(resolved);
            if (!loaded.Success)
            {
                throw SyntaxError(machine, $"{resolved}: {string.Join("; ", loaded.Diagnostics)}");
            }
        }
        finally
        {
            _loadingSourceFiles.Remove(resolved);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Text is pulled a line at a time until the lexer finds a clause terminator, so a term can be
    /// read from a console as soon as it is complete rather than after the input ends. Whatever
    /// follows the terminator stays in the buffer, which belongs to the stream.
    /// </remarks>
    public bool TryReadTerm(
        Machine machine,
        TextReader input,
        ref string buffer,
        out Cell term,
        out Cell variableNames,
        out Cell variables,
        out Cell singletons
    )
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(input);

        term = default;
        variableNames = Cell.Atom(machine.Symbols.EmptyList);
        variables = Cell.Atom(machine.Symbols.EmptyList);
        singletons = Cell.Atom(machine.Symbols.EmptyList);

        int end = ClauseScanner.FindClauseEnd(buffer, Program.CharacterConversions, Program.Flags);
        while (end < 0)
        {
            string? chunk = ReadLinePreservingTerminator(input);
            if (chunk is null)
            {
                // What is left at end of input is either nothing, which is end_of_file, or a clause
                // missing its terminator. Reading the incomplete text as if it were whole would
                // quietly return a prefix of what the file says.
                bool blank = ClauseScanner.IsBlank(buffer, Program.CharacterConversions, Program.Flags);
                buffer = string.Empty;

                return blank ? false : throw SyntaxError(machine, "unexpected_end_of_file");
            }

            buffer += chunk;
            end = ClauseScanner.FindClauseEnd(buffer, Program.CharacterConversions, Program.Flags);
        }

        string text = buffer[..end];
        buffer = buffer[end..];

        ParseResult parsed = ReadTerm(text);
        if (!parsed.Success || parsed.Clauses.Count == 0)
        {
            string error = parsed.Diagnostics.Count > 0 ? parsed.Diagnostics[0].Id : "cannot_start_term";
            throw error switch
            {
                DiagnosticIds.MaxIntegerExceeded => PrologErrors.Representation(machine, "max_integer"),
                DiagnosticIds.MinIntegerExceeded => PrologErrors.Representation(machine, "min_integer"),
                DiagnosticIds.FloatOverflow => SyntaxError(machine, "float_overflow"),
                _ => SyntaxError(machine, error),
            };
        }

        Dictionary<string, Cell> namedVariables = [];
        List<Cell> variableOrder = [];
        term = TermReifier.ToHeap(
            machine,
            TermNormalizer.Normalize(parsed.Clauses[0], Program.Flags.DoubleQuotes),
            namedVariables,
            variableOrder
        );

        // Only named variables are reported, and in the order the reader met them, so that
        // variable_names/1 and singletons/1 read the way the source does.
        List<Cell> pairs = [];
        List<Cell> singletonPairs = [];
        int equals = machine.Symbols.InternFunctor("=", 2);
        foreach (NamedVariable named in CollectNamedVariables(parsed.Clauses[0]))
        {
            if (namedVariables.TryGetValue(named.Name, out Cell variable))
            {
                Cell pair = machine.CreateStructure(equals, [Cell.Atom(machine.Symbols.InternAtom(named.Name)), variable]);
                pairs.Add(pair);

                if (named.Count == 1)
                {
                    singletonPairs.Add(pair);
                }
            }
        }

        variableNames = machine.CreateList(pairs.ToArray(), Cell.Atom(machine.Symbols.EmptyList));
        variables = machine.CreateList(variableOrder.ToArray(), Cell.Atom(machine.Symbols.EmptyList));
        singletons = machine.CreateList(singletonPairs.ToArray(), Cell.Atom(machine.Symbols.EmptyList));
        return true;
    }

    private static string? ReadLinePreservingTerminator(TextReader input)
    {
        var chunk = new System.Text.StringBuilder();

        while (true)
        {
            int value = input.Read();
            if (value < 0)
            {
                return chunk.Length == 0 ? null : chunk.ToString();
            }

            char character = (char)value;
            chunk.Append(character);
            if (character is '\r' or '\n')
            {
                return chunk.ToString();
            }
        }
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
