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
public sealed class PrologEngine
{
    private readonly List<int> _pendingDirectives = [];
    private readonly List<int> _pendingInitialization = [];

    /// <summary>Creates an engine with the core builtins registered and an empty program.</summary>
    public PrologEngine()
    {
        Program = new BytecodeProgram();
        CoreBuiltins.RegisterAll(Program);
        Machine = new Machine(Program);

        LoadResult bootstrap = new ProgramLoader(Program).Load(ReadOrThrow(BootstrapLibrary.Source, "bootstrap"), "bootstrap");

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

        LoadResult loaded = new ProgramLoader(Program).Load(parsed.Clauses, fileName);
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

    /// <summary>Proves <paramref name="goalText"/>, read as a single term, and reports the outcome.</summary>
    /// <param name="goalText">A goal in Prolog syntax, with or without a trailing full stop.</param>
    /// <param name="diagnostics">Diagnostics raised while reading or compiling the goal.</param>
    public RunResult RunGoal(string goalText, out IReadOnlyList<Diagnostic> diagnostics)
    {
        int address = CompileGoal(goalText, out diagnostics);
        return address < 0 ? RunResult.Failure : Machine.Run(address);
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
