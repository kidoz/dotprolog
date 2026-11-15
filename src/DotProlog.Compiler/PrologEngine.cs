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
public sealed class PrologEngine : IRuntimeCompiler, IReclaimableClauseCompiler, IDisposable
{
    private const int ControlGoalCacheLimit = 1024;
    internal const int MaxSourceChainDepth = 64;
    internal const string SourceChainDepthExceeded = "DPL1020";

    private readonly ModuleTable _modules;
    private readonly List<int> _pendingDirectives = [];
    private readonly List<int> _pendingInitialization = [];
    private int _pendingMainInitialization = -1;
    private List<int>? _generatedInitialization;
    private List<int>? _generatedMainInitialization;
    private Action<int>? _generatedDirectiveObserver;
    private readonly HashSet<string> _loadedSourceFiles = new(PathComparer);
    private readonly HashSet<string> _loadingSourceFiles = new(PathComparer);
    private readonly HashSet<string> _failedSourceFiles = new(PathComparer);
    private readonly Dictionary<string, (int Address, int ArgumentCount)> _controlGoals = new(StringComparer.Ordinal);
    private bool _preparationHalted;

    internal CompilationSourceMap? SourceMap { get; set; }

    /// <summary>Creates a modern-mode engine with the core builtins registered and an empty program.</summary>
    public PrologEngine()
        : this(PrologLanguageMode.Modern) { }

    /// <summary>
    /// Creates an engine using <paramref name="languageMode"/>, with
    /// <paramref name="flagOverrides"/> layered over the mode's initial flag values.
    /// </summary>
    public PrologEngine(PrologLanguageMode languageMode, PrologFlagOverrides? flagOverrides = null)
    {
        Program = new BytecodeProgram(languageMode, flagOverrides);
        _modules = new ModuleTable(Program.Modules);
        CoreBuiltins.RegisterAll(Program);
        Machine = new Machine(Program);
        Program.RuntimeCompiler = this;

        // The bundled libraries are processor implementation, not user source, so they are read under
        // codes and Modern's operand reading whatever mode or override the host chose; no dialect
        // can reinterpret them.
        var isoReading = Program.Flags.IsoReading;
        var digitSeparators = Program.Flags.DigitSeparators;
        Program.Flags.DoubleQuotes = DoubleQuotesMode.Codes;
        Program.Flags.IsoReading = false;
        Program.Flags.DigitSeparators = true;
        LoadLibrary(BootstrapLibrary.Source, "bootstrap");
        LoadLibrary(StandardLibrary.Source, "library");
        Program.Flags.DoubleQuotes = Program.InitialDoubleQuotes;
        Program.Flags.IsoReading = isoReading;
        Program.Flags.DigitSeparators = digitSeparators;
    }

    /// <summary>Compiles one of the built-in libraries, which must not fail.</summary>
    private void LoadLibrary(string source, string name)
    {
        var loaded = new ProgramLoader(Program, Machine, _modules, userPredicates: false).Load(ReadOrThrow(source, name), name);

        if (!loaded.Success)
        {
            throw new PrologException($"The {name} library failed to compile: {string.Join("; ", loaded.Diagnostics)}");
        }
    }

    private IReadOnlyList<SyntaxTerm> ReadOrThrow(string source, string fileName)
    {
        var parsed = TermReader.ReadProgram(source, fileName, Program.Operators, Program.CharacterConversions, Program.Flags);
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

    /// <summary>Closes program-owned streams, leaving host-owned standard streams open.</summary>
    /// <remarks>Call after the engine has stopped executing; the engine is not thread-safe.</remarks>
    public void Dispose() => Machine.Dispose();

    /// <summary>The program this engine loads into.</summary>
    public BytecodeProgram Program { get; }

    /// <summary>Explicit library aliases resolved by use_module(library(Name)); no network access occurs.</summary>
    public IReadOnlyDictionary<string, string> LibraryFiles { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

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
        return ConsultFile(path, 0);
    }

    private LoadResult ConsultFile(string path, int sourceDepth)
    {
        var absolute = Path.GetFullPath(path);
        _failedSourceFiles.Remove(absolute);
        var loaded = ConsultText(File.ReadAllText(absolute), absolute, sourceDepth);
        if (loaded.Success)
        {
            _loadedSourceFiles.Add(absolute);
        }
        else if (!_loadedSourceFiles.Contains(absolute))
        {
            _failedSourceFiles.Add(absolute);
        }

        return loaded;
    }

    internal LoadResult ConsultDependency(string path, int sourceDepth) => ConsultFile(path, sourceDepth);

    internal bool HasFailedSource(string path) => _failedSourceFiles.Contains(path);

    internal bool NeedsEnsureLoaded(string path) => !_loadedSourceFiles.Contains(path) && !_loadingSourceFiles.Contains(path);

    /// <summary>Reads and compiles <paramref name="text"/> as a Prolog source unit.</summary>
    /// <param name="text">Prolog source.</param>
    /// <param name="fileName">File name used in diagnostics, when known.</param>
    public LoadResult ConsultText(string text, string? fileName = null) => ConsultText(text, fileName, 0);

    private LoadResult ConsultText(string text, string? fileName, int sourceDepth)
    {
        ArgumentNullException.ThrowIfNull(text);

        var sourceSites = new SourceLoadContext();
        var parsed = ReadProgramWithIncludes(text, fileName, sourceDepth, sourceSites);
        if (!parsed.Success)
        {
            return new LoadResult(parsed.Diagnostics, [], []);
        }

        var loader = new ProgramLoader(Program, Machine, _modules, userPredicates: true, libraryFiles: LibraryFiles)
        {
            SourceMap = SourceMap,
            SourceSiteOf = sourceSites.SiteOf,
        };
        Func<int, RunResult>? directiveExecutor =
            Machine.IsRunning ? null
            : _generatedDirectiveObserver is not null ? ObserveGeneratedDirective
            : ExecutePreparationDirective;
        var loaded = loader.Load(parsed.Clauses, fileName, directiveExecutor);
        List<Diagnostic> diagnostics = [.. parsed.Diagnostics, .. loaded.Diagnostics];

        // Which module a file declared is what use_module/1 needs to know when it is imported later.
        if (loaded.Success && fileName is not null && File.Exists(fileName))
        {
            var absolute = Path.GetFullPath(fileName);
            _modules.RecordLoad(absolute, loader.Module);
            _loadedSourceFiles.Add(absolute);
        }

        if (loaded.Success)
        {
            if (_generatedInitialization is null)
            {
                _pendingDirectives.AddRange(loaded.DirectiveAddresses);
                EnqueueInitialization(loaded.InitializationAddresses, loader.MainInitializationAddress);
            }
            else
            {
                CollectGeneratedInitialization(
                    loaded.InitializationAddresses,
                    loader.MainInitializationAddress,
                    _generatedInitialization,
                    _generatedMainInitialization!
                );
            }
        }

        return new LoadResult(diagnostics, loaded.DirectiveAddresses, loaded.InitializationAddresses);
    }

    /// <summary>
    /// Compiles a build-time source while reporting each executable directive at its publication
    /// point. Generated backends use the snapshots to reproduce source preparation at application
    /// startup.
    /// </summary>
    internal LoadResult CompileForGeneratedCode(
        string text,
        string? fileName,
        Action<int> directiveObserver,
        List<int> initialization,
        List<int> mainInitialization
    )
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(directiveObserver);
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(mainInitialization);

        var sourceSites = new SourceLoadContext();
        var parsed = ReadProgramWithIncludes(text, fileName, 0, sourceSites);
        if (!parsed.Success)
        {
            return new LoadResult(parsed.Diagnostics, [], []);
        }

        var previousObserver = _generatedDirectiveObserver;
        var previousInitialization = _generatedInitialization;
        var previousMain = _generatedMainInitialization;
        _generatedDirectiveObserver = directiveObserver;
        _generatedInitialization = initialization;
        _generatedMainInitialization = mainInitialization;
        try
        {
            var loader = new ProgramLoader(Program, Machine, _modules, userPredicates: true, libraryFiles: LibraryFiles)
            {
                SourceMap = SourceMap,
                SourceSiteOf = sourceSites.SiteOf,
            };
            var loaded = loader.Load(parsed.Clauses, fileName, ObserveGeneratedDirective);
            List<Diagnostic> diagnostics = [.. parsed.Diagnostics, .. loaded.Diagnostics];
            if (loaded.Success)
            {
                CollectGeneratedInitialization(
                    loaded.InitializationAddresses,
                    loader.MainInitializationAddress,
                    initialization,
                    mainInitialization
                );
            }

            return new LoadResult(diagnostics, loaded.DirectiveAddresses, loaded.InitializationAddresses);
        }
        finally
        {
            _generatedDirectiveObserver = previousObserver;
            _generatedInitialization = previousInitialization;
            _generatedMainInitialization = previousMain;
        }
    }

    private RunResult ObserveGeneratedDirective(int address)
    {
        _generatedDirectiveObserver!(address);
        return RunResult.Success;
    }

    private void EnqueueInitialization(IReadOnlyList<int> addresses, int main)
    {
        foreach (var address in addresses)
        {
            if (address != main)
            {
                _pendingInitialization.Add(address);
            }
        }

        if (main >= 0)
        {
            _pendingMainInitialization = main;
        }
    }

    private static void CollectGeneratedInitialization(
        IReadOnlyList<int> addresses,
        int main,
        List<int> initialization,
        List<int> mainInitialization
    )
    {
        foreach (var address in addresses)
        {
            if (address != main)
            {
                initialization.Add(address);
            }
        }

        if (main >= 0)
        {
            mainInitialization.Clear();
            mainInitialization.Add(main);
        }
    }

    /// <summary>
    /// Reads a source unit while expanding ISO <c>include/1</c> declarations at their source
    /// position. All nested readers share the program's reader state.
    /// </summary>
    private ParseResult ReadProgramWithIncludes(string text, string? fileName, int sourceDepth, SourceLoadContext sourceSites)
    {
        HashSet<string> active = new(PathComparer);
        var moduleReaderState = new IsoModuleReaderState(Program);
        var rootPath = ExistingFullPath(fileName);
        if (rootPath is not null)
        {
            active.Add(rootPath);
        }

        try
        {
            return ReadSource(text, fileName, sourceDepth);
        }
        finally
        {
            moduleReaderState.Complete();
        }

        ParseResult ReadSource(string source, string? sourceName, int depth)
        {
            SourceMap?.Register(sourceName, source);
            return TermReader.ReadProgram(
                source,
                sourceName,
                Program.Operators,
                Program.CharacterConversions,
                Program.Flags,
                clause => ExpandInclude(clause, sourceName, depth),
                clause =>
                {
                    sourceSites.Register(clause, depth, sourceName);
                    SourceMap?.SetDocument(clause, sourceName);
                    moduleReaderState.AtBoundary(clause);
                },
                moduleReaderState.AfterReaderDirective
            );
        }

        ParseResult? ExpandInclude(SyntaxTerm clause, string? includingFile, int includingDepth)
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

            var path = ResolveSourcePath(file.Name, includingFile);
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
                if (includingDepth >= MaxSourceChainDepth)
                {
                    return IncludeError(
                        SourceChainDepthExceeded,
                        $"Source dependency chain exceeds the maximum depth of {MaxSourceChainDepth}.",
                        file.Span,
                        includingFile
                    );
                }

                return ReadSource(File.ReadAllText(path), path, includingDepth + 1);
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

    private sealed class SourceLoadContext
    {
        private readonly Dictionary<SyntaxTerm, (int Depth, string? FileName)> _sites = new(ReferenceEqualityComparer.Instance);

        internal void Register(SyntaxTerm clause, int depth, string? fileName) => _sites[clause] = (depth, fileName);

        internal (int Depth, string? FileName) SiteOf(SyntaxTerm clause) =>
            _sites.TryGetValue(clause, out var site) ? site : (0, null);
    }

    /// <summary>Switches the shared lexer state at ISO interface and body boundaries.</summary>
    private sealed class IsoModuleReaderState
    {
        private readonly BytecodeProgram _program;
        private readonly OperatorTable _rootOperators;
        private readonly CharacterConversionTable _rootConversions;
        private readonly PrologFlags _rootFlags;
        private readonly Stack<string> _enclosingModules = [];
        private readonly HashSet<string> _startedBodies = new(StringComparer.Ordinal);
        private string? _activeModule;
        private bool _implicitUserActive;
        private bool _moduleText;
        private bool _pendingImplicitUser;
        private bool _inInterface;

        internal IsoModuleReaderState(BytecodeProgram program)
        {
            _program = program;
            _rootOperators = program.Operators.Copy();
            _rootConversions = program.CharacterConversions.Copy();
            _rootFlags = program.Flags.Copy();
        }

        internal void AtBoundary(SyntaxTerm clause)
        {
            if (clause is not CompoundTerm { Name: ":-", Arity: 1, Arguments: [CompoundTerm marker] })
            {
                ObserveImplicitUserTerm();
                return;
            }

            if (marker.Name is "module" or "end_module" or "body" or "end_body" && marker.Arity == 1)
            {
                if (!_moduleText)
                {
                    _moduleText = true;
                    MaterializePendingImplicitUserBody();
                }
            }

            if (marker is { Name: "module" or "body", Arity: 1, Arguments: [AtomTerm startName] })
            {
                if (_implicitUserActive)
                {
                    CloseImplicitUserBody();
                }

                if (_activeModule is not null)
                {
                    if (marker.Name != "body" || _activeModule != ModuleTable.UserModule)
                    {
                        return;
                    }

                    if (_program.Modules.TryGet(_activeModule, out var enclosing))
                    {
                        enclosing!.SeedReaderState(_program.Operators, _program.CharacterConversions, _program.Flags);
                    }

                    _enclosingModules.Push(_activeModule);
                }
                else
                {
                    CaptureRoot();
                }

                var startDefinition = _program.Modules.Declare(startName.Name);
                if (marker.Name == "module")
                {
                    startDefinition.SeedReaderState(_rootOperators, _rootConversions, _rootFlags);
                }
                else
                {
                    if (startName.Name == ModuleTable.UserModule && _startedBodies.Add(startName.Name))
                    {
                        startDefinition.SeedReaderState(_rootOperators, _rootConversions, _rootFlags);
                    }

                    startDefinition.RecordPreparedBody();
                }

                Install(startDefinition);
                _inInterface = marker.Name == "module";
                _activeModule = startName.Name;
                return;
            }

            if (
                marker is { Name: "end_module" or "end_body", Arity: 1, Arguments: [AtomTerm endName] }
                && _activeModule == endName.Name
                && _program.Modules.TryGet(endName.Name, out var endDefinition)
            )
            {
                if (!_inInterface)
                {
                    endDefinition!.SeedReaderState(_program.Operators, _program.CharacterConversions, _program.Flags);
                }

                _inInterface = false;
                if (_enclosingModules.TryPop(out var enclosingModule))
                {
                    var enclosing = _program.Modules.Declare(enclosingModule);
                    enclosing.RecordPreparedBody();
                    Install(enclosing);
                    _activeModule = enclosingModule;
                }
                else
                {
                    if (endName.Name == ModuleTable.UserModule)
                    {
                        CaptureRoot();
                    }

                    Restore();
                    _activeModule = null;
                }

                return;
            }

            ObserveImplicitUserTerm();

            if (_inInterface && _activeModule is not null)
            {
                // The clause was parsed using the enclosing text state. Apply its declarations
                // to the future body state, then restore the interface reader before advancing.
                Install(_program.Modules.Declare(_activeModule));
            }

            ApplyFlagDirective(marker);
        }

        internal void AfterReaderDirective(SyntaxTerm clause)
        {
            if (_inInterface && _activeModule is not null)
            {
                _program
                    .Modules.Declare(_activeModule)
                    .SeedReaderState(_program.Operators, _program.CharacterConversions, _program.Flags);
                Restore();
            }
        }

        internal void Restore()
        {
            _program.Operators.ReplaceWith(_rootOperators);
            _program.CharacterConversions.ReplaceWith(_rootConversions);
            _program.Flags.ReplaceWith(_rootFlags);
        }

        internal void Complete()
        {
            if (_implicitUserActive)
            {
                CloseImplicitUserBody();
            }

            Restore();
        }

        private void Install(ModuleDefinition definition)
        {
            _program.Operators.ReplaceWith(definition.Operators);
            _program.CharacterConversions.ReplaceWith(definition.CharacterConversions);
            _program.Flags.ReplaceWith(definition.Flags);
        }

        private void CaptureRoot()
        {
            _rootOperators.ReplaceWith(_program.Operators);
            _rootConversions.ReplaceWith(_program.CharacterConversions);
            _rootFlags.ReplaceWith(_program.Flags);
        }

        private void StartImplicitUserBody()
        {
            CaptureRoot();
            var user = _program.Modules.Declare(ModuleTable.UserModule);
            if (_startedBodies.Add(ModuleTable.UserModule))
            {
                user.SeedReaderState(_rootOperators, _rootConversions, _rootFlags);
            }

            user.RecordPreparedBody();
            Install(user);
            _activeModule = ModuleTable.UserModule;
            _implicitUserActive = true;
        }

        private void ObserveImplicitUserTerm()
        {
            if (_activeModule is not null)
            {
                return;
            }

            if (_moduleText)
            {
                StartImplicitUserBody();
            }
            else
            {
                _pendingImplicitUser = true;
            }
        }

        private void MaterializePendingImplicitUserBody()
        {
            if (!_pendingImplicitUser)
            {
                return;
            }

            var user = _program.Modules.Declare(ModuleTable.UserModule);
            user.SeedReaderState(_rootOperators, _rootConversions, _rootFlags);
            user.RecordPreparedBody();
            user.SeedReaderState(_program.Operators, _program.CharacterConversions, _program.Flags);
            _startedBodies.Add(ModuleTable.UserModule);
            CaptureRoot();
            _pendingImplicitUser = false;
        }

        private void CloseImplicitUserBody()
        {
            var user = _program.Modules.Declare(ModuleTable.UserModule);
            user.SeedReaderState(_program.Operators, _program.CharacterConversions, _program.Flags);
            CaptureRoot();
            _activeModule = null;
            _implicitUserActive = false;
        }

        private void ApplyFlagDirective(CompoundTerm goal)
        {
            if (goal is not { Name: "set_prolog_flag", Arity: 2, Arguments: [AtomTerm flag, AtomTerm value] })
            {
                return;
            }

            switch (flag.Name, value.Name)
            {
                case ("char_conversion", "on"):
                    _program.Flags.SetCharConversion(true);
                    break;
                case ("char_conversion", "off"):
                    _program.Flags.SetCharConversion(false);
                    break;
                case ("debug", "on"):
                    _program.Flags.Debug = true;
                    break;
                case ("debug", "off"):
                    _program.Flags.Debug = false;
                    break;
                case ("double_quotes", "codes"):
                    _program.Flags.DoubleQuotes = DoubleQuotesMode.Codes;
                    break;
                case ("double_quotes", "chars"):
                    _program.Flags.DoubleQuotes = DoubleQuotesMode.Chars;
                    break;
                case ("double_quotes", "atom"):
                    _program.Flags.DoubleQuotes = DoubleQuotesMode.Atom;
                    break;
                case ("double_quotes", "string") when _program.LanguageMode != PrologLanguageMode.StrictIso:
                    _program.Flags.DoubleQuotes = DoubleQuotesMode.String;
                    break;
                case ("unknown", "error"):
                    _program.Flags.Unknown = UnknownProcedureAction.Error;
                    break;
                case ("unknown", "warning"):
                    _program.Flags.Unknown = UnknownProcedureAction.Warning;
                    break;
                case ("unknown", "fail"):
                    _program.Flags.Unknown = UnknownProcedureAction.Fail;
                    break;
            }
        }
    }

    private static ParseResult IncludeError(string id, string message, SourceSpan span, string? fileName) =>
        new([], [new Diagnostic(id, DiagnosticSeverity.Error, message, span, fileName)]);

    private static string? ExistingFullPath(string? fileName) =>
        fileName is not null && File.Exists(fileName) ? Path.GetFullPath(fileName) : null;

    private static string? ResolveSourcePath(string name, string? includingFile)
    {
        var directory = includingFile is null
            ? Directory.GetCurrentDirectory()
            : (Path.GetDirectoryName(Path.GetFullPath(includingFile)) ?? ".");

        foreach (var candidate in (string[])[name, name + ".pl"])
        {
            var absolute = Path.IsPathRooted(candidate) ? candidate : Path.Combine(directory, candidate);
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
        var loaded = ConsultText(text, fileName);
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
    /// <remarks>
    /// A goal that consults a file queues that file's directives and initialization goals, since the
    /// machine cannot run them while it is busy. They run as soon as the goal ends, ahead of the goals
    /// already waiting, which is as close as a queue gets to running them inside the consult.
    /// </remarks>
    public RunResult RunPendingGoals()
    {
        if (_preparationHalted)
        {
            _preparationHalted = false;
            ClearPendingGoals();
            return RunResult.Halted;
        }

        while (_pendingDirectives.Count > 0 || _pendingInitialization.Count > 0 || _pendingMainInitialization >= 0)
        {
            var directive = _pendingDirectives.Count > 0;
            int address;
            if (directive)
            {
                address = _pendingDirectives[0];
                _pendingDirectives.RemoveAt(0);
            }
            else if (_pendingInitialization.Count > 0)
            {
                address = _pendingInitialization[0];
                _pendingInitialization.RemoveAt(0);
            }
            else
            {
                address = _pendingMainInitialization;
                _pendingMainInitialization = -1;
            }

            var waiting = _pendingInitialization.Count;
            var result = Machine.Run(address);
            if (result == RunResult.Halted)
            {
                ClearPendingGoals();
                return result;
            }

            if (result == RunResult.Failure)
            {
                Output.Write(directive ? "Warning: directive failed.\n" : "Warning: initialization goal failed.\n");
            }

            var queued = _pendingInitialization.Count - waiting;
            if (queued > 0)
            {
                var consulted = _pendingInitialization.GetRange(waiting, queued);
                _pendingInitialization.RemoveRange(waiting, queued);
                _pendingInitialization.InsertRange(0, consulted);
            }
        }

        return RunResult.Success;
    }

    /// <summary>Runs one generated source-position directive and any goals queued by runtime consultation.</summary>
    /// <remarks>Generated-code contract. Failure retains the ordinary directive warning policy.</remarks>
    public RunResult RunPreparationDirective(int address)
    {
        var result = ExecutePreparationDirective(address);
        if (result == RunResult.Halted)
        {
            return result;
        }

        var pending = RunPendingGoals();
        if (pending == RunResult.Halted)
        {
            _preparationHalted = true;
        }

        return pending == RunResult.Halted ? pending : result;
    }

    /// <summary>Runs generated initialization goals and drains goals queued by runtime consultation.</summary>
    /// <remarks>
    /// Generated-code contract. Initialization failure stops the sequence; halt stops it immediately and
    /// preserves the requested exit code. Ordinary consulted initialization keeps
    /// <see cref="RunPendingGoals"/>'s warning-and-continue policy.
    /// </remarks>
    public RunResult RunInitializers(IReadOnlyList<int> initialization)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        var pending = RunPendingGoals();
        if (pending == RunResult.Halted)
        {
            return pending;
        }

        foreach (var target in initialization)
        {
            var result = Machine.Run(target);
            if (result != RunResult.Halted)
            {
                pending = RunPendingGoals();
                if (pending == RunResult.Halted)
                {
                    result = pending;
                }
            }

            if (result != RunResult.Success)
            {
                if (result == RunResult.Halted)
                {
                    ClearPendingGoals();
                }

                return result;
            }
        }

        return RunResult.Success;
    }

    private void ClearPendingGoals()
    {
        _pendingDirectives.Clear();
        _pendingInitialization.Clear();
        _pendingMainInitialization = -1;
    }

    private RunResult ExecutePreparationDirective(int address)
    {
        var result = Machine.Run(address);
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

        var parsed = ReadTerm(goalText);
        if (!parsed.Success || parsed.Clauses.Count == 0)
        {
            diagnostics = parsed.Diagnostics;
            return -1;
        }

        List<Diagnostic> compileDiagnostics = [];
        var compiler = new ClauseCompiler(Program, new ConstantPool(Program), compileDiagnostics, null);
        var address = compiler.Compile(new AtomTerm("$goal", parsed.Clauses[0].Span), parsed.Clauses[0]);
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

        var parsed = ReadTerm(goalText);
        if (!parsed.Success || parsed.Clauses.Count == 0)
        {
            throw new PrologException($"The goal did not parse: {string.Join("; ", parsed.Diagnostics)}");
        }

        var goal = parsed.Clauses[0];
        var names = CollectVariableNames(goal);
        var body = goal;

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
        var address = compiler.Compile(new AtomTerm("$query", goal.Span), body);

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
            var term = pending[^1];
            pending.RemoveAt(pending.Count - 1);

            switch (term)
            {
                // '_' is deliberately not reported: each occurrence is a different variable.
                case VariableTerm { IsAnonymous: false } variable:
                    if (counts.TryGetValue(variable.Name, out var count))
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
                    for (var i = compound.Arity - 1; i >= 0; i--)
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
        var address = CompileGoal(goalText, out diagnostics);
        return address < 0 ? RunResult.Failure : Machine.Run(address);
    }

    /// <inheritdoc />
    /// <remarks>
    /// This is the meta-called-control path. The anonymous clause receives the original live
    /// variables as arguments, so compiling the body in place preserves both aliasing and ISO cut
    /// scope without adding control state to the runtime machine. The compiled code depends only on
    /// the goal's shape with variables numbered by first occurrence, so one clause per shape is
    /// cached: anonymous control code stays pinned, and recompiling the same shape on every
    /// meta-call would grow it without bound.
    /// </remarks>
    public int CompileControlGoal(Machine machine, Cell goal, Span<Cell> arguments, out int argumentCount)
    {
        ArgumentNullException.ThrowIfNull(machine);

        var key = ControlGoalKey(machine, goal, out var goalVariables);

        if (_controlGoals.TryGetValue(key, out var cached))
        {
            for (var i = 0; i < cached.ArgumentCount; i++)
            {
                arguments[i] = goalVariables[i];
            }

            argumentCount = cached.ArgumentCount;
            return cached.Address;
        }

        var variables = new Dictionary<string, Cell>(StringComparer.Ordinal);
        var body = TermReifier.ToControlSyntax(machine, goal, variables);
        var names = CollectVariableNames(body);

        if (names.Length >= Machine.ArgumentRegisterCount || names.Length > arguments.Length)
        {
            throw PrologErrors.Representation(machine, "max_arity");
        }

        var headArguments = new SyntaxTerm[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            headArguments[i] = new VariableTerm(name, SourceSpan.None);
            arguments[i] = variables[name];
        }

        SyntaxTerm head =
            headArguments.Length == 0
                ? new AtomTerm("$meta_control", SourceSpan.None)
                : new CompoundTerm("$meta_control", headArguments, SourceSpan.None);

        List<Diagnostic> diagnostics = [];
        var compiler = new ClauseCompiler(Program, new ConstantPool(Program), diagnostics, null);
        var address = compiler.Compile(head, body);
        argumentCount = names.Length;

        if (address < 0)
        {
            if (
                diagnostics.Any(diagnostic => diagnostic.Id == CompilerDiagnosticIds.StrictIsoViolation)
                && StrictControlError(machine, body) is { } strictError
            )
            {
                throw strictError;
            }

            throw PrologErrors.Type(machine, "callable", goal);
        }

        if (_controlGoals.Count < ControlGoalCacheLimit)
        {
            _controlGoals[key] = (address, names.Length);
        }

        return address;
    }

    private PrologException? StrictControlError(Machine machine, SyntaxTerm goal)
    {
        List<SyntaxTerm> pending = [goal];
        while (pending.Count > 0)
        {
            var current = pending[^1];
            pending.RemoveAt(pending.Count - 1);
            if (current is not (AtomTerm or CompoundTerm))
            {
                continue;
            }

            var name = current is AtomTerm atom ? atom.Name : ((CompoundTerm)current).Name;
            var arity = current is CompoundTerm compound ? compound.Arity : 0;
            var functor = Program.Symbols.InternFunctor(name, arity);
            if (name == "*->" && arity == 2 || Program.IsStrictIsoExtension(functor))
            {
                return PrologErrors.Permission(machine, "access", "implementation_specific_feature", functor);
            }

            if (current is not CompoundTerm control || name == "call")
            {
                continue;
            }

            if ((arity == 2 && name is "," or ";" or "->" or "*->") || (arity == 1 && name == "\\+"))
            {
                for (var i = control.Arity - 1; i >= 0; i--)
                {
                    pending.Add(control.Arguments[i]);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the cache key for a meta-called control term — its structure with constants by
    /// interned identity and variables numbered by first occurrence — and collects those variables
    /// in the same order <see cref="CollectVariableNames"/> reaches them in the reified goal.
    /// </summary>
    private const byte ControlPosition = 0;
    private const byte LeafPosition = 1;
    private const byte LeavingPosition = 2;

    // The key walks the goal exactly as TermReifier.ToControlSyntax does — depth-first, left to
    // right, one ordinal per distinct cell address, structures in leaf positions abstracted the
    // same way variables are — so the variable list stays aligned with the compiled head's
    // first-occurrence argument order.
    private static string ControlGoalKey(Machine machine, Cell goal, out List<Cell> variables)
    {
        var key = new System.Text.StringBuilder();
        Dictionary<int, int> ordinals = [];
        HashSet<int> active = [];
        variables = [];
        List<(Cell Cell, byte Position)> work = [(goal, ControlPosition)];

        while (work.Count > 0)
        {
            (var source, var position) = work[^1];
            work.RemoveAt(work.Count - 1);

            if (position == LeavingPosition)
            {
                active.Remove(source.Index);
                key.Append(')');
                continue;
            }

            var cell = machine.Dereference(source);
            switch (cell.Tag)
            {
                case CellTag.Reference:
                    AppendOrdinal(key, cell, ordinals, variables);
                    break;

                case CellTag.Atom:
                    key.Append('a').Append(cell.Index).Append(',');
                    break;

                case CellTag.Integer:
                    key.Append('i').Append(cell.Integer).Append(',');
                    break;

                case CellTag.BigInteger:
                    key.Append('b').Append(cell.Index).Append(',');
                    break;

                case CellTag.Rational:
                    key.Append('q').Append(cell.Index).Append(',');
                    break;

                case CellTag.Float:
                    key.Append('f').Append(cell.Index).Append(',');
                    break;

                case CellTag.Structure:
                {
                    // A structure filling a leaf-goal argument travels through a register like a
                    // variable, so it is abstracted rather than walked; a cyclic term there is
                    // legal because it is never reified.
                    if (position == LeafPosition)
                    {
                        AppendOrdinal(key, cell, ordinals, variables);
                        break;
                    }

                    // A rational control skeleton cannot be reified into finite syntax; reject it
                    // with a catchable error rather than looping here or overflowing later.
                    if (!active.Add(cell.Index))
                    {
                        throw PrologErrors.Representation(machine, "cyclic_term");
                    }

                    var functorId = machine.HeapAt(cell.Index).Index;
                    var functor = machine.Symbols.GetFunctor(functorId);
                    var name = machine.Symbols.AtomName(functor.NameAtom);
                    var control =
                        (functor.Arity == 2 && name is "," or ";" or "->" or "*->") || (functor.Arity == 1 && name == "\\+");

                    key.Append('s').Append(functorId).Append('(');
                    work.Add((cell, LeavingPosition));
                    for (var i = functor.Arity; i >= 1; i--)
                    {
                        work.Add((machine.HeapAt(cell.Index + i), control ? ControlPosition : LeafPosition));
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

    private static void AppendOrdinal(
        System.Text.StringBuilder key,
        Cell cell,
        Dictionary<int, int> ordinals,
        List<Cell> variables
    )
    {
        if (!ordinals.TryGetValue(cell.Index, out var ordinal))
        {
            ordinal = variables.Count;
            ordinals[cell.Index] = ordinal;
            variables.Add(cell);
        }

        key.Append('V').Append(ordinal).Append(',');
    }

    /// <inheritdoc />
    /// <remarks>
    /// This is the <c>assertz/1</c> path. It compiles into the same program the goal is running from,
    /// and the dispatch loop refreshes its cached arrays after every builtin. Public compilation
    /// stays pinned; assertion can request ownership of private code before publishing a clause.
    /// </remarks>
    public int CompileClause(Machine machine, Cell clause, out int functorId) =>
        CompileClause(machine, clause, out functorId, false, out _);

    int IReclaimableClauseCompiler.CompileDynamicClause(
        Machine machine,
        Cell clause,
        out int functorId,
        out DynamicCodeBlock storage
    ) => CompileClause(machine, clause, out functorId, true, out storage);

    private int CompileClause(Machine machine, Cell clause, out int functorId, bool reclaimableCode, out DynamicCodeBlock storage)
    {
        storage = default;
        ArgumentNullException.ThrowIfNull(machine);

        // The clause is inspected as cells before it is reified, because an error raised here has
        // to name the offending part of the term the program passed, and catch/3 can only unify
        // against a term the machine built.
        var whole = machine.Dereference(clause);
        if (whole.Tag == CellTag.Reference)
        {
            throw PrologErrors.Instantiation(machine);
        }

        var headCell = whole;
        Cell bodyCell = default;
        var hasBody = false;
        var rule2 = machine.Symbols.InternFunctor(":-", 2);

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

        var term = TermReifier.ToSyntax(machine, clause);
        var head = term;
        SyntaxTerm? body = null;
        List<Diagnostic> diagnostics = [];

        // A grammar rule asserted at run time is translated exactly as one read from a file, so
        // assertz((greeting --> [hello])) defines greeting//0 rather than a clause of -->/2.
        if (term is CompoundTerm { Name: "-->", Arity: 2 } grammarRule)
        {
            if (!DcgTranslator.TryTranslate(grammarRule, diagnostics, null, Program.LanguageMode, out head, out var translated))
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

        var compiledHeadName = head switch
        {
            AtomTerm atom => atom.Name,
            CompoundTerm compound => compound.Name,
            _ => string.Empty,
        };
        var moduleSeparator = compiledHeadName.LastIndexOf(':');
        if (moduleSeparator > 0)
        {
            var module = compiledHeadName[..moduleSeparator];
            var sourceName = compiledHeadName[(moduleSeparator + 1)..];
            var arity = head is CompoundTerm compound ? compound.Arity : 0;
            var indicator = new PredicateIndicator(sourceName, arity);
            _modules.Define(module, indicator);
            if (body is not null)
            {
                body = new ModuleResolver(_modules, module, [indicator], Program).ResolveGoal(body);
            }
        }

        functorId = head switch
        {
            AtomTerm atom => Program.Symbols.InternFunctor(atom.Name, 0),
            CompoundTerm compound => Program.Symbols.InternFunctor(compound.Name, compound.Arity),
            _ => throw PrologErrors.Type(machine, "callable", headCell),
        };

        var compiler = new ClauseCompiler(
            Program,
            reclaimableCode ? null : new ConstantPool(Program),
            diagnostics,
            null,
            reclaimableCode: reclaimableCode
        );
        var address = compiler.Compile(head, body);
        storage = compiler.CodeStorage;

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

        var resolved =
            ResolveSourcePath(path, null)
            ?? throw ExistenceError(machine, "source_sink", Cell.Atom(machine.Symbols.InternAtom(path)));
        var loaded = ConsultFile(resolved);
        if (!loaded.Success)
        {
            throw SyntaxError(machine, $"{resolved}: {string.Join("; ", loaded.Diagnostics)}");
        }
    }

    /// <inheritdoc />
    public void EnsureLoadedFile(Machine machine, string path) => EnsureLoadedFile(machine, path, 0);

    internal void EnsureLoadedFile(Machine machine, string path, int sourceDepth)
    {
        var loaded = EnsureLoadedDependency(machine, path, sourceDepth);
        if (loaded is { Success: false })
        {
            throw SyntaxError(machine, $"{path}: {string.Join("; ", loaded.Diagnostics)}");
        }
    }

    internal LoadResult? EnsureLoadedDependency(Machine machine, string path, int sourceDepth)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(path);

        var resolved =
            ResolveSourcePath(path, null)
            ?? throw ExistenceError(machine, "source_sink", Cell.Atom(machine.Symbols.InternAtom(path)));
        if (_loadedSourceFiles.Contains(resolved) || !_loadingSourceFiles.Add(resolved))
        {
            return null;
        }

        try
        {
            return ConsultFile(resolved, sourceDepth);
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
    ) =>
        TryReadTerm(
            machine,
            input,
            ref buffer,
            Program.Operators,
            Program.CharacterConversions,
            Program.Flags,
            out term,
            out variableNames,
            out variables,
            out singletons
        );

    /// <inheritdoc />
    public bool TryReadTerm(
        Machine machine,
        TextReader input,
        ref string buffer,
        OperatorTable operators,
        CharacterConversionTable characterConversions,
        PrologFlags flags,
        out Cell term,
        out Cell variableNames,
        out Cell variables,
        out Cell singletons
    )
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(operators);
        ArgumentNullException.ThrowIfNull(characterConversions);
        ArgumentNullException.ThrowIfNull(flags);

        term = default;
        variableNames = Cell.Atom(machine.Symbols.EmptyList);
        variables = Cell.Atom(machine.Symbols.EmptyList);
        singletons = Cell.Atom(machine.Symbols.EmptyList);

        var incremental = new IncrementalTermInput(buffer, characterConversions, flags);
        while (true)
        {
            var end = incremental.FindClauseEnd();
            if (end >= 0)
            {
                var text = incremental.Take(end, out buffer);
                return ReifyReadTerm(
                    machine,
                    text,
                    operators,
                    characterConversions,
                    flags,
                    out term,
                    out variableNames,
                    out variables,
                    out singletons
                );
            }

            var chunk = ReadLinePreservingTerminator(input);
            if (chunk is not null)
            {
                incremental.Append(chunk);
                continue;
            }

            end = incremental.FindClauseEndAtEndOfInput();
            if (end >= 0)
            {
                var text = incremental.Take(end, out buffer);
                return ReifyReadTerm(
                    machine,
                    text,
                    operators,
                    characterConversions,
                    flags,
                    out term,
                    out variableNames,
                    out variables,
                    out singletons
                );
            }

            // What is left at end of input is either nothing, which is end_of_file, or a clause
            // missing its terminator. Reading the incomplete text as if it were whole would
            // quietly return a prefix of what the file says.
            var blank = ClauseScanner.IsBlank(incremental.RemainingText(), characterConversions, flags);
            buffer = string.Empty;

            return blank ? false : throw SyntaxError(machine, "unexpected_end_of_file");
        }
    }

    private static bool ReifyReadTerm(
        Machine machine,
        string text,
        OperatorTable operators,
        CharacterConversionTable characterConversions,
        PrologFlags flags,
        out Cell term,
        out Cell variableNames,
        out Cell variables,
        out Cell singletons
    )
    {
        var parsed = TermReader.ReadTerm(text, operators: operators, characterConversions: characterConversions, flags: flags);
        if (!parsed.Success || parsed.Clauses.Count == 0)
        {
            var error = parsed.Diagnostics.Count > 0 ? parsed.Diagnostics[0].Id : "cannot_start_term";
            throw error switch
            {
                DiagnosticIds.FloatOverflow => SyntaxError(machine, "float_overflow"),
                _ => SyntaxError(machine, error),
            };
        }

        Dictionary<string, Cell> namedVariables = [];
        List<Cell> variableOrder = [];
        term = TermReifier.ToHeap(
            machine,
            TermNormalizer.Normalize(parsed.Clauses[0], flags.DoubleQuotes, flags.CodePointCharacters),
            namedVariables,
            variableOrder
        );

        // Only named variables are reported, and in the order the reader met them, so that
        // variable_names/1 and singletons/1 read the way the source does.
        List<Cell> pairs = [];
        List<Cell> singletonPairs = [];
        var equals = machine.Symbols.InternFunctor("=", 2);
        foreach (var named in CollectNamedVariables(parsed.Clauses[0]))
        {
            if (namedVariables.TryGetValue(named.Name, out var variable))
            {
                var pair = machine.CreateStructure(equals, [Cell.Atom(machine.Symbols.InternAtom(named.Name)), variable]);
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

    private sealed class IncrementalTermInput
    {
        private const string SymbolCharacters = "+-*/\\^<>=~:.?@#&$";

        private readonly System.Text.StringBuilder _text = new();
        private readonly CharacterConversionTable _characterConversions;
        private readonly PrologFlags _flags;
        private int _position;
        private ScanState _state;
        private char _quote;
        private int _numberStart;

        internal IncrementalTermInput(string text, CharacterConversionTable characterConversions, PrologFlags flags)
        {
            _text.Append(text);
            _characterConversions = characterConversions;
            _flags = flags;
        }

        internal void Append(string text) => _text.Append(text);

        internal string RemainingText() => _text.ToString();

        internal string Take(int end, out string remainder)
        {
            remainder = _text.ToString(end, _text.Length - end);
            return _text.ToString(0, end);
        }

        internal int FindClauseEndAtEndOfInput()
        {
            var end = FindClauseEnd();
            return end >= 0 ? end : ClauseScanner.FindClauseEnd(_text.ToString(), _characterConversions, _flags);
        }

        internal int FindClauseEnd()
        {
            while (_position < _text.Length)
            {
                switch (_state)
                {
                    case ScanState.Quoted:
                        if (ReadQuotedCharacter())
                        {
                            continue;
                        }

                        return -1;

                    case ScanState.QuoteEscape:
                        _position++;
                        _state = ScanState.Quoted;
                        continue;

                    case ScanState.LineComment:
                    {
                        var code = ConvertedAt(_position, out var width);
                        if (width == 0)
                        {
                            return -1;
                        }

                        _position += width;
                        if (code == '\n')
                        {
                            _state = ScanState.Normal;
                        }

                        continue;
                    }

                    case ScanState.BlockComment:
                    {
                        var code = ConvertedAt(_position, out var width);
                        if (width == 0)
                        {
                            return -1;
                        }

                        if (code == '*')
                        {
                            if (!TryConvertedAt(_position + width, out var next, out var nextWidth) || nextWidth == 0)
                            {
                                return -1;
                            }

                            if (next == '/')
                            {
                                _position += width + nextWidth;
                                _state = ScanState.Normal;
                                continue;
                            }
                        }

                        _position += width;
                        continue;
                    }

                    case ScanState.CharacterCode:
                        if (ReadCharacterCode())
                        {
                            continue;
                        }

                        return -1;

                    case ScanState.CharacterCodeEscape:
                        _position++;
                        _state = ScanState.Normal;
                        continue;

                    case ScanState.Number:
                    {
                        var code = ConvertedAt(_position, out var width);
                        if (code == '.')
                        {
                            if (!TryConvertedAt(_position + width, out var next, out _) || next < 0)
                            {
                                return -1;
                            }

                            if (next is >= '0' and <= '9')
                            {
                                _position += width;
                                continue;
                            }

                            _state = ScanState.Symbol;
                            continue;
                        }

                        if (_text[_position] == '\'' && _position == _numberStart + 1 && ConvertedAt(_numberStart, out _) == '0')
                        {
                            if (!TryCharacterCodeStart(_position, out var starts))
                            {
                                return -1;
                            }

                            if (starts)
                            {
                                _position++;
                                _state = ScanState.CharacterCode;
                                continue;
                            }
                        }

                        if (code is >= '0' and <= '9' || code == '_' || code is >= 'A' and <= 'Z' || code is >= 'a' and <= 'z')
                        {
                            _position += width;
                            continue;
                        }

                        _state = ScanState.Normal;
                        continue;
                    }

                    case ScanState.Symbol:
                    {
                        var code = ConvertedAt(_position, out var width);
                        if (!IsSymbol(code))
                        {
                            _state = ScanState.Normal;
                            continue;
                        }

                        if (code != '.')
                        {
                            _position += width;
                            continue;
                        }

                        if (!TryConvertedAt(_position + width, out var next, out var nextWidth) || nextWidth == 0)
                        {
                            return -1;
                        }

                        if (!IsSymbol(next) && (IsLayout(next) || next == '%'))
                        {
                            var candidate = _position + width;
                            var confirmed = ClauseScanner.FindClauseEnd(_text.ToString(), _characterConversions, _flags);
                            if (confirmed >= 0)
                            {
                                return confirmed;
                            }

                            _position = candidate;
                            _state = ScanState.Normal;
                            continue;
                        }

                        _position += width;
                        continue;
                    }

                    default:
                    {
                        var code = ConvertedAt(_position, out var width);
                        if (width == 0)
                        {
                            return -1;
                        }

                        if (IsLayout(code))
                        {
                            _position += width;
                            continue;
                        }

                        if (code == '%')
                        {
                            _position += width;
                            _state = ScanState.LineComment;
                            continue;
                        }

                        if (code == '/')
                        {
                            if (!TryConvertedAt(_position + width, out var next, out var nextWidth) || nextWidth == 0)
                            {
                                return -1;
                            }

                            if (next == '*')
                            {
                                _position += width + nextWidth;
                                _state = ScanState.BlockComment;
                                continue;
                            }
                        }

                        var raw = _text[_position];
                        if (raw is '\'' or '"' or '`')
                        {
                            _position++;
                            _quote = raw;
                            _state = ScanState.Quoted;
                            continue;
                        }

                        if (code is >= '0' and <= '9')
                        {
                            _numberStart = _position;
                            _position += width;
                            _state = ScanState.Number;
                            continue;
                        }

                        if (IsSymbol(code))
                        {
                            _state = ScanState.Symbol;
                            continue;
                        }

                        _position += width;
                        continue;
                    }
                }
            }

            return -1;
        }

        private bool ReadQuotedCharacter()
        {
            var current = _text[_position];
            if (current == '\\')
            {
                _position++;
                _state = ScanState.QuoteEscape;
                return true;
            }

            if (current != _quote)
            {
                _position++;
                return true;
            }

            if (_position + 1 >= _text.Length)
            {
                return false;
            }

            if (_text[_position + 1] == _quote)
            {
                _position += 2;
                return true;
            }

            _position++;
            _state = ScanState.Normal;
            return true;
        }

        private bool ReadCharacterCode()
        {
            var current = _text[_position];
            if (current == '\'')
            {
                if (_position + 1 >= _text.Length)
                {
                    return false;
                }

                _position += 2;
                _state = ScanState.Normal;
                return true;
            }

            if (current == '\\')
            {
                _position++;
                _state = ScanState.CharacterCodeEscape;
                return true;
            }

            var width =
                _flags.CodePointCharacters
                && char.IsHighSurrogate(current)
                && _position + 1 < _text.Length
                && char.IsLowSurrogate(_text[_position + 1])
                    ? 2
                    : 1;
            _position += width;
            _state = ScanState.Normal;
            return true;
        }

        private bool TryCharacterCodeStart(int quote, out bool starts)
        {
            starts = false;
            if (quote + 1 >= _text.Length)
            {
                return false;
            }

            var next = _text[quote + 1];
            if (next is not ('\'' or '\\'))
            {
                starts = true;
                return true;
            }

            if (quote + 2 >= _text.Length)
            {
                return false;
            }

            starts = next == '\'' ? _text[quote + 2] == '\'' : _text[quote + 2] != '\n';
            return true;
        }

        private int ConvertedAt(int position, out int width)
        {
            TryConvertedAt(position, out var code, out width);
            return code;
        }

        private bool TryConvertedAt(int position, out int code, out int width)
        {
            code = -1;
            width = 1;
            if (position < 0 || position >= _text.Length)
            {
                return false;
            }

            var current = _text[position];
            code = current;
            if (current is '\'' or '"' or '`')
            {
                return true;
            }

            if (_flags.CodePointCharacters && char.IsHighSurrogate(current))
            {
                if (position + 1 >= _text.Length)
                {
                    width = 0;
                    return true;
                }

                if (char.IsLowSurrogate(_text[position + 1]))
                {
                    code = char.ConvertToUtf32(current, _text[position + 1]);
                    width = 2;
                }
            }

            if (_flags.CodePointCharacters && char.IsSurrogate(current))
            {
                return true;
            }

            if (_flags.CharConversion)
            {
                code = _characterConversions.Convert(code);
            }

            return true;
        }

        private bool IsLayout(int code) =>
            code >= 0
            && (
                _flags.CodePointCharacters
                    ? code <= char.MaxValue
                        ? char.IsWhiteSpace((char)code)
                        : System.Text.Rune.IsWhiteSpace(new System.Text.Rune(code))
                    : code is ' ' or '\t' or '\n' or '\v' or '\f' or '\r'
            );

        private static bool IsSymbol(int code) =>
            code >= 0 && code <= char.MaxValue && SymbolCharacters.Contains((char)code, StringComparison.Ordinal);

        private enum ScanState
        {
            Normal,
            Quoted,
            QuoteEscape,
            LineComment,
            BlockComment,
            Number,
            CharacterCode,
            CharacterCodeEscape,
            Symbol,
        }
    }

    private static string? ReadLinePreservingTerminator(TextReader input)
    {
        var chunk = new System.Text.StringBuilder();

        while (true)
        {
            var value = input.Read();
            if (value < 0)
            {
                return chunk.Length == 0 ? null : chunk.ToString();
            }

            var character = (char)value;
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
        var formal = machine.CreateStructure(
            machine.Symbols.InternFunctor("syntax_error", 1),
            [Cell.Atom(machine.Symbols.InternAtom(what))]
        );

        var error = machine.CreateStructure(machine.Symbols.InternFunctor("error", 2), [formal, machine.CreateVariable()]);
        return machine.CreateBall(error, $"syntax_error({what})");
    }

    /// <summary>Builds a catchable <c>error(existence_error(Kind, Culprit), _)</c>.</summary>
    private static PrologException ExistenceError(Machine machine, string kind, Cell culprit)
    {
        var formal = machine.CreateStructure(
            machine.Symbols.InternFunctor("existence_error", 2),
            [Cell.Atom(machine.Symbols.InternAtom(kind)), culprit]
        );

        var error = machine.CreateStructure(machine.Symbols.InternFunctor("error", 2), [formal, machine.CreateVariable()]);
        return machine.CreateBall(error, $"existence_error({kind}, {TermWriter.ToDisplayString(machine, culprit)})");
    }
}
