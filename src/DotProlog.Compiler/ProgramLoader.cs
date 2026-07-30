using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>
/// Lowers read clauses into a <see cref="BytecodeProgram"/>: groups clauses by predicate, chains the
/// alternatives with try/retry/trust, and compiles directives into anonymous goal blocks.
/// </summary>
public sealed class ProgramLoader
{
    private readonly BytecodeProgram _program;
    private readonly ConstantPool _constants;
    private readonly Machine? _machine;
    private readonly ModuleTable _modules;
    private readonly bool _userPredicates;

    /// <summary>Creates a loader that appends to <paramref name="program"/>.</summary>
    /// <param name="program">The program to load into.</param>
    /// <param name="machine">
    /// Machine used to build the clause terms a dynamic predicate needs for <c>retract/1</c>. Without
    /// one, a <c>:- dynamic</c> declaration is reported rather than honoured.
    /// </param>
    /// <param name="modules">
    /// The program's modules. Without one, every file loads into <c>user</c>, which is what a caller
    /// that only wants to compile a term wants.
    /// </param>
    /// <param name="userPredicates">
    /// Whether predicates emitted by this loader came from user source. The engine's bundled
    /// libraries pass <see langword="false"/> so ISO predicate enumeration excludes them.
    /// </param>
    public ProgramLoader(
        BytecodeProgram program,
        Machine? machine = null,
        ModuleTable? modules = null,
        bool userPredicates = true
    )
    {
        ArgumentNullException.ThrowIfNull(program);
        _program = program;
        _constants = new ConstantPool(program);
        _machine = machine;
        _modules = modules ?? new ModuleTable();
        _userPredicates = userPredicates;
    }

    /// <summary>Lowers <paramref name="clauses"/> into the program.</summary>
    /// <param name="clauses">Clauses and directives in source order.</param>
    /// <param name="fileName">File name used in diagnostics, when known.</param>
    /// <param name="directiveExecutor">
    /// Optional executor for ordinary directives. When present, each directive runs at its source
    /// position; otherwise its address is returned for a non-reentrant runtime consult to queue.
    /// </param>
    public LoadResult Load(
        IReadOnlyList<SyntaxTerm> clauses,
        string? fileName = null,
        Func<int, RunResult>? directiveExecutor = null
    )
    {
        ArgumentNullException.ThrowIfNull(clauses);

        List<Diagnostic> diagnostics = [];
        List<int> directives = [];
        List<int> initialization = [];

        // Reading happens in two passes. The first settles which module the unit belongs to and what
        // it defines; only then can a call in a body be told from one to somewhere else.
        var unit = new LoadUnit();
        Collect(clauses, unit, diagnostics, fileName);
        DeclareMultifilePredicates(unit, diagnostics, fileName);

        var resolver = new ModuleResolver(_modules, unit.Module, unit.Defines);
        HashSet<int> dynamicPredicates = DeclareDynamicPredicates(unit, resolver, diagnostics, fileName);
        Dictionary<int, List<(SyntaxTerm Head, SyntaxTerm? Body)>> accumulated = [];
        Dictionary<int, List<(SyntaxTerm Head, SyntaxTerm? Body)>> pending = [];
        List<int> dirtyOrder = [];
        HashSet<int> dirty = [];
        bool halted = false;

        foreach (LoadItem item in unit.Items)
        {
            if (item is ClauseItem clause)
            {
                SyntaxTerm resolvedHead = resolver.ResolveHead(clause.Head);
                SyntaxTerm? resolvedBody = clause.Body is null ? null : resolver.ResolveGoal(clause.Body);

                if (!TryGetHeadFunctor(resolvedHead, out int functorId))
                {
                    Report(
                        diagnostics,
                        CompilerDiagnosticIds.InvalidClauseHead,
                        "A clause head must be an atom or a compound term.",
                        clause.Head.Span,
                        fileName
                    );
                    continue;
                }

                if (!accumulated.TryGetValue(functorId, out List<(SyntaxTerm, SyntaxTerm?)>? allClauses))
                {
                    allClauses = [];
                    accumulated[functorId] = allClauses;
                }

                allClauses.Add((resolvedHead, resolvedBody));

                if (!pending.TryGetValue(functorId, out List<(SyntaxTerm, SyntaxTerm?)>? newClauses))
                {
                    newClauses = [];
                    pending[functorId] = newClauses;
                }

                newClauses.Add((resolvedHead, resolvedBody));
                if (dirty.Add(functorId))
                {
                    dirtyOrder.Add(functorId);
                }

                continue;
            }

            FlushPredicates();
            SyntaxTerm goal = ((DirectiveItem)item).Goal;
            if (goal is CompoundTerm { Name: "ensure_loaded", Arity: 1 } ensureLoaded)
            {
                EnsureLoaded(ensureLoaded, diagnostics, fileName);
                continue;
            }

            int address = CompileDirective(resolver.ResolveGoal(goal), diagnostics, out bool deferred, fileName);
            if (address < 0)
            {
                continue;
            }

            if (deferred)
            {
                initialization.Add(address);
                continue;
            }

            if (directiveExecutor is null)
            {
                directives.Add(address);
                continue;
            }

            RunResult result = directiveExecutor(address);
            if (result == RunResult.Halted)
            {
                halted = true;
                break;
            }
        }

        if (!halted)
        {
            FlushPredicates();
        }

        PublishExports(unit);
        Module = unit.Module;
        return new LoadResult(diagnostics, directives, initialization);

        void FlushPredicates()
        {
            foreach (int functorId in dirtyOrder)
            {
                if (dynamicPredicates.Contains(functorId) || _program.IsDynamic(functorId))
                {
                    EmitDynamicClauses(functorId, pending[functorId], diagnostics, fileName);
                    continue;
                }

                PredicateIndicator sourceIndicator = SourceIndicatorOf(unit.Module, functorId);
                if (_modules.IsMultifile(unit.Module, sourceIndicator))
                {
                    IReadOnlyList<(SyntaxTerm Head, SyntaxTerm? Body)> multifileClauses =
                        _modules.AppendMultifileClauses(unit.Module, sourceIndicator, pending[functorId]);
                    EmitPredicate(functorId, multifileClauses, diagnostics, fileName);
                    continue;
                }

                EmitPredicate(functorId, accumulated[functorId], diagnostics, fileName);
            }

            pending.Clear();
            dirty.Clear();
            dirtyOrder.Clear();
        }
    }

    /// <summary>The module the last load declared, or <c>user</c>.</summary>
    public string Module { get; private set; } = ModuleTable.UserModule;

    /// <summary>
    /// Makes each exported predicate reachable by its plain name as well as its qualified one.
    /// </summary>
    /// <remarks>
    /// This is what keeps a generated facade, the command line tool, and a program that simply
    /// consults a module working without knowing modules exist. The plain name is only claimed when
    /// nothing else has it, so the first module to export a name gets it and a second one is reached
    /// by qualifying it or by importing it.
    /// </remarks>
    private void PublishExports(LoadUnit unit)
    {
        if (unit.Module == ModuleTable.UserModule)
        {
            return;
        }

        foreach (PredicateIndicator export in _modules.ExportsOf(unit.Module))
        {
            if (!unit.Defines.Contains(export))
            {
                continue;
            }

            int qualified = _program.Symbols.InternFunctor(ModuleTable.QualifiedName(unit.Module, export.Name), export.Arity);

            _program.AliasPredicate(_program.Symbols.InternFunctor(export.Name, export.Arity), qualified);
        }
    }

    /// <summary>Runs the first pass: declarations, and the set of predicates the unit defines.</summary>
    private void Collect(IReadOnlyList<SyntaxTerm> clauses, LoadUnit unit, List<Diagnostic> diagnostics, string? fileName)
    {
        DoubleQuotesMode doubleQuotes = _program.Flags.DoubleQuotes;

        foreach (SyntaxTerm rawClause in clauses)
        {
            SyntaxTerm clause = TermNormalizer.Normalize(rawClause, doubleQuotes);

            if (clause is CompoundTerm { Name: ":-", Arity: 1 } directive)
            {
                SyntaxTerm goal = directive.Arguments[0];

                switch (goal)
                {
                    case CompoundTerm { Name: "module", Arity: 2 } declaration:
                        DeclareModule(declaration, unit, diagnostics, fileName);
                        continue;

                    case CompoundTerm { Name: "use_module", Arity: 1 or 2 } import:
                        UseModule(import, unit, diagnostics, fileName);
                        continue;

                    case CompoundTerm { Name: "meta_predicate", Arity: 1 } meta:
                        DeclareMeta(meta.Arguments[0], diagnostics, fileName);
                        continue;

                    case CompoundTerm { Name: "multifile", Arity: 1 } multifile:
                        unit.Multifile.Add(multifile.Arguments[0]);
                        AddDeclaredPredicates(multifile.Arguments[0], unit.Defines);
                        continue;

                    case CompoundTerm { Name: "discontiguous", Arity: 1 } discontiguous:
                        ValidateDeclaration(
                            discontiguous.Arguments[0],
                            CompilerDiagnosticIds.InvalidDiscontiguousDeclaration,
                            "discontiguous",
                            diagnostics,
                            fileName
                        );
                        continue;

                    // A dynamic declaration changes how later clauses are stored, so it is collected
                    // here and acted on once the module is known.
                    case CompoundTerm { Name: "dynamic", Arity: 1 } dynamic:
                        unit.Dynamic.Add(dynamic.Arguments[0]);

                        // A dynamic predicate belongs to the module whether or not the file holds a
                        // clause of it, so a call to it resolves the same way as one with clauses.
                        foreach (SyntaxTerm indicator in Indicators(dynamic.Arguments[0]))
                        {
                            if (TryIndicator(indicator, out PredicateIndicator declared))
                            {
                                unit.Defines.Add(declared);
                            }
                        }

                        continue;

                    default:
                        break;
                }

                if (TryDoubleQuotesDirective(goal, out DoubleQuotesMode selected))
                {
                    doubleQuotes = selected;
                    _program.Flags.DoubleQuotes = selected;
                }

                unit.Items.Add(new DirectiveItem(goal));
                continue;
            }

            SyntaxTerm head = clause;
            SyntaxTerm? body = null;

            // A grammar rule becomes an ordinary clause before anything else looks at it, so the
            // rest of the loader and the whole compiler never learn that DCGs exist.
            if (clause is CompoundTerm { Name: "-->", Arity: 2 } grammarRule)
            {
                if (!DcgTranslator.TryTranslate(grammarRule, diagnostics, fileName, out head, out SyntaxTerm translated))
                {
                    continue;
                }

                body = translated;
            }
            else if (clause is CompoundTerm { Name: ":-", Arity: 2 } rule)
            {
                head = rule.Arguments[0];
                body = rule.Arguments[1];
            }

            switch (head)
            {
                case AtomTerm atom:
                    unit.Defines.Add(new PredicateIndicator(atom.Name, 0));
                    break;

                case CompoundTerm compound:
                    unit.Defines.Add(new PredicateIndicator(compound.Name, compound.Arity));
                    break;

                default:
                    Report(
                        diagnostics,
                        CompilerDiagnosticIds.InvalidClauseHead,
                        "A clause head must be an atom or a compound term.",
                        head.Span,
                        fileName
                    );
                    continue;
            }

            unit.Items.Add(new ClauseItem(head, body));
        }
    }

    /// <summary>Loads an <c>ensure_loaded/1</c> declaration once, relative to its source unit.</summary>
    private void EnsureLoaded(CompoundTerm declaration, List<Diagnostic> diagnostics, string? fileName)
    {
        if (declaration.Arguments[0] is not AtomTerm file)
        {
            Report(
                diagnostics,
                CompilerDiagnosticIds.InvalidEnsureLoadedDeclaration,
                "ensure_loaded/1 needs an atom naming a source file.",
                declaration.Arguments[0].Span,
                fileName
            );
            return;
        }

        string? path = ResolvePath(file.Name, fileName);
        if (path is null || _program.RuntimeCompiler is null || _machine is null)
        {
            Report(
                diagnostics,
                CompilerDiagnosticIds.EnsureLoadedNotFound,
                $"No file for ensure_loaded({file.Name}).",
                file.Span,
                fileName
            );
            return;
        }

        try
        {
            _program.RuntimeCompiler.EnsureLoadedFile(_machine, path);
        }
        catch (PrologException exception)
        {
            Report(
                diagnostics,
                CompilerDiagnosticIds.EnsureLoadedNotFound,
                exception.Message,
                file.Span,
                fileName
            );
        }
    }

    private static void AddDeclaredPredicates(SyntaxTerm declarations, HashSet<PredicateIndicator> defines)
    {
        foreach (SyntaxTerm item in Indicators(declarations))
        {
            if (TryIndicator(item, out PredicateIndicator indicator))
            {
                defines.Add(indicator);
            }
        }
    }

    private static void ValidateDeclaration(
        SyntaxTerm declarations,
        string diagnosticId,
        string declarationName,
        List<Diagnostic> diagnostics,
        string? fileName
    )
    {
        foreach (SyntaxTerm item in Indicators(declarations))
        {
            if (TryIndicator(item, out _))
            {
                continue;
            }

            Report(
                diagnostics,
                diagnosticId,
                $"{declarationName}/1 expected a predicate indicator of the form Name/Arity or Name//Arity.",
                item.Span,
                fileName
            );
        }
    }

    private void DeclareModule(CompoundTerm declaration, LoadUnit unit, List<Diagnostic> diagnostics, string? fileName)
    {
        if (declaration.Arguments[0] is not AtomTerm name)
        {
            Report(
                diagnostics,
                CompilerDiagnosticIds.InvalidModuleDeclaration,
                "A module name must be an atom.",
                declaration.Span,
                fileName
            );
            return;
        }

        unit.Module = name.Name;
        List<PredicateIndicator> exports = [];

        foreach (SyntaxTerm export in Indicators(declaration.Arguments[1]))
        {
            if (TryIndicator(export, out PredicateIndicator indicator))
            {
                exports.Add(indicator);
                continue;
            }

            Report(
                diagnostics,
                CompilerDiagnosticIds.InvalidModuleDeclaration,
                "An export must be Name/Arity or Name//Arity.",
                export.Span,
                fileName
            );
        }

        _modules.Declare(name.Name, exports);
    }

    /// <summary>Loads the file a <c>use_module</c> names and imports what it exports.</summary>
    private void UseModule(CompoundTerm import, LoadUnit unit, List<Diagnostic> diagnostics, string? fileName)
    {
        if (import.Arguments[0] is not AtomTerm file)
        {
            Report(
                diagnostics,
                CompilerDiagnosticIds.InvalidModuleDeclaration,
                "use_module needs a file name.",
                import.Span,
                fileName
            );
            return;
        }

        string? path = ResolvePath(file.Name, fileName);
        if (path is null)
        {
            Report(
                diagnostics,
                CompilerDiagnosticIds.ModuleNotFound,
                $"No file for use_module({file.Name}).",
                import.Span,
                fileName
            );
            return;
        }

        string? loaded = _modules.LoadedModuleOf(path);
        if (loaded is null)
        {
            if (_program.RuntimeCompiler is null || _machine is null)
            {
                Report(
                    diagnostics,
                    CompilerDiagnosticIds.ModuleNotFound,
                    "use_module needs an engine to load the file into.",
                    import.Span,
                    fileName
                );
                return;
            }

            _modules.BeginLoad(path);
            _program.RuntimeCompiler.ConsultFile(_machine, path);
            loaded = _modules.LoadedModuleOf(path) ?? ModuleTable.UserModule;
        }

        // An explicit import list narrows what is taken; without one, everything the module exports
        // is imported, which is what use_module/1 means.
        IEnumerable<PredicateIndicator> wanted = _modules.ExportsOf(loaded);

        if (import.Arity == 2)
        {
            List<PredicateIndicator> listed = [];
            foreach (SyntaxTerm item in Indicators(import.Arguments[1]))
            {
                if (TryIndicator(item, out PredicateIndicator indicator))
                {
                    listed.Add(indicator);
                }
            }

            wanted = listed;
        }

        foreach (PredicateIndicator indicator in wanted)
        {
            _modules.Import(unit.Module, indicator, loaded);
        }
    }

    /// <summary>Finds the file a <c>use_module</c> names, relative to the file that asked for it.</summary>
    private static string? ResolvePath(string name, string? fileName)
    {
        string directory = fileName is null
            ? Directory.GetCurrentDirectory()
            : (Path.GetDirectoryName(Path.GetFullPath(fileName)) ?? ".");

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

    private void DeclareMeta(SyntaxTerm specification, List<Diagnostic> diagnostics, string? fileName)
    {
        foreach (SyntaxTerm item in Indicators(specification))
        {
            if (item is not CompoundTerm spec)
            {
                Report(
                    diagnostics,
                    CompilerDiagnosticIds.InvalidModuleDeclaration,
                    "A meta_predicate specification must be a compound term.",
                    item.Span,
                    fileName
                );
                continue;
            }

            List<(int Position, int Extra)> arguments = [];
            for (int i = 0; i < spec.Arity; i++)
            {
                // An integer says the argument is a closure gaining that many arguments; ':' says it
                // is module-sensitive but not called here. Anything else is an ordinary argument.
                switch (spec.Arguments[i])
                {
                    case IntegerTerm count when count.Value >= 0:
                        arguments.Add((i, (int)count.Value));
                        break;

                    case AtomTerm { Name: ":" }:
                        arguments.Add((i, 1));
                        break;

                    default:
                        break;
                }
            }

            _modules.DeclareMeta(spec.Name, spec.Arity, arguments);
        }
    }

    /// <summary>Acts on the unit's dynamic declarations, now that the module is known.</summary>
    private HashSet<int> DeclareDynamicPredicates(
        LoadUnit unit,
        ModuleResolver resolver,
        List<Diagnostic> diagnostics,
        string? fileName
    )
    {
        HashSet<int> declared = [];

        foreach (SyntaxTerm indicators in unit.Dynamic)
        {
            DeclareDynamic(indicators, declared, diagnostics, fileName, unit.Module);
        }

        _ = resolver;
        return declared;
    }

    /// <summary>Validates and persists the unit's static multifile declarations.</summary>
    private void DeclareMultifilePredicates(LoadUnit unit, List<Diagnostic> diagnostics, string? fileName)
    {
        foreach (SyntaxTerm declarations in unit.Multifile)
        {
            foreach (SyntaxTerm item in Indicators(declarations))
            {
                if (!TryIndicator(item, out PredicateIndicator indicator))
                {
                    Report(
                        diagnostics,
                        CompilerDiagnosticIds.InvalidMultifileDeclaration,
                        "multifile/1 expected a predicate indicator of the form Name/Arity or Name//Arity.",
                        item.Span,
                        fileName
                    );
                    continue;
                }

                _modules.DeclareMultifile(unit.Module, indicator);
            }
        }
    }

    /// <summary>The items of a comma sequence or a list, which is how these declarations are written.</summary>
    private static IEnumerable<SyntaxTerm> Indicators(SyntaxTerm term)
    {
        switch (term)
        {
            case CompoundTerm { Name: ",", Arity: 2 } sequence:
                foreach (SyntaxTerm item in Indicators(sequence.Arguments[0]))
                {
                    yield return item;
                }

                foreach (SyntaxTerm item in Indicators(sequence.Arguments[1]))
                {
                    yield return item;
                }

                break;

            case CompoundTerm { Name: ".", Arity: 2 } cons:
                yield return cons.Arguments[0];

                foreach (SyntaxTerm item in Indicators(cons.Arguments[1]))
                {
                    yield return item;
                }

                break;

            case AtomTerm { Name: "[]" }:
                break;

            default:
                yield return term;
                break;
        }
    }

    /// <summary>Reads <c>Name/Arity</c>, or <c>Name//Arity</c> which names a grammar rule.</summary>
    private static bool TryIndicator(SyntaxTerm term, out PredicateIndicator indicator)
    {
        indicator = default;

        if (term is not CompoundTerm { Arity: 2 } slash || slash.Name is not ("/" or "//"))
        {
            return false;
        }

        if (slash.Arguments[0] is not AtomTerm name || slash.Arguments[1] is not IntegerTerm arity)
        {
            return false;
        }

        long compiledArity = arity.Value + (slash.Name == "//" ? 2 : 0);
        if (compiledArity is < 0 or >= Machine.ArgumentRegisterCount)
        {
            return false;
        }

        // A grammar rule is compiled with two extra arguments, so that is what its predicate is.
        indicator = new PredicateIndicator(name.Name, (int)compiledArity);
        return true;
    }

    /// <summary>What one unit of loading knows about itself.</summary>
    private sealed class LoadUnit
    {
        internal string Module { get; set; } = ModuleTable.UserModule;

        internal HashSet<PredicateIndicator> Defines { get; } = [];

        internal List<SyntaxTerm> Dynamic { get; } = [];

        internal List<SyntaxTerm> Multifile { get; } = [];

        internal List<LoadItem> Items { get; } = [];
    }

    private abstract record LoadItem;

    private sealed record ClauseItem(SyntaxTerm Head, SyntaxTerm? Body) : LoadItem;

    private sealed record DirectiveItem(SyntaxTerm Goal) : LoadItem;

    /// <summary>
    /// Applies a valid <c>double_quotes</c> directive while collecting, because it changes how
    /// subsequent reader tokens in the same source unit are represented. The directive is still
    /// compiled and run normally so the ordinary builtin owns validation and final runtime state.
    /// </summary>
    private static bool TryDoubleQuotesDirective(SyntaxTerm goal, out DoubleQuotesMode selected)
    {
        selected = default;
        if (
            goal
            is not CompoundTerm
            {
                Name: "set_prolog_flag",
                Arity: 2,
                Arguments: [AtomTerm { Name: "double_quotes" }, AtomTerm value],
            }
        )
        {
            return false;
        }

        selected = value.Name switch
        {
            "codes" => DoubleQuotesMode.Codes,
            "chars" => DoubleQuotesMode.Chars,
            "atom" => DoubleQuotesMode.Atom,
            _ => default,
        };

        return value.Name is "codes" or "chars" or "atom";
    }

    /// <summary>Handles <c>:- dynamic Name/Arity</c>, a comma sequence of them, or a list of them.</summary>
    private void DeclareDynamic(
        SyntaxTerm indicators,
        HashSet<int> declared,
        List<Diagnostic> diagnostics,
        string? fileName,
        string module
    )
    {
        foreach (SyntaxTerm term in Indicators(indicators))
        {
            if (!TryIndicator(term, out PredicateIndicator indicator))
            {
                Report(
                    diagnostics,
                    CompilerDiagnosticIds.InvalidDynamicDeclaration,
                    "Expected a predicate indicator of the form Name/Arity or Name//Arity.",
                    term.Span,
                    fileName
                );
                continue;
            }

            if (_machine is null)
            {
                Report(
                    diagnostics,
                    CompilerDiagnosticIds.DynamicNotAvailable,
                    "A dynamic declaration needs a machine to load into.",
                    term.Span,
                    fileName
                );
                continue;
            }

            int functorId = _program.Symbols.InternFunctor(
                ModuleTable.QualifiedName(module, indicator.Name),
                indicator.Arity
            );
            _program.DeclareDynamic(functorId, _userPredicates);
            declared.Add(functorId);
        }
    }

    /// <summary>Compiles each clause of a dynamic predicate separately and adds it to the database.</summary>
    private void EmitDynamicClauses(
        int functorId,
        List<(SyntaxTerm Head, SyntaxTerm? Body)> clauses,
        List<Diagnostic> diagnostics,
        string? fileName
    )
    {
        DynamicPredicate predicate = _program.DeclareDynamic(functorId, _userPredicates);
        Machine machine = _machine!;
        int rule = _program.Symbols.InternFunctor(":-", 2);

        foreach ((SyntaxTerm head, SyntaxTerm? body) in clauses)
        {
            var compiler = new ClauseCompiler(_program, _constants, diagnostics, fileName);
            int address = compiler.Compile(head, body);
            if (address < 0)
            {
                continue;
            }

            // retract/1 matches against the clause as a term, so keep a detached copy of it.
            Dictionary<string, Cell> variables = [];
            Cell headCell = TermReifier.ToHeap(machine, head, variables);
            Cell bodyCell = body is null ? Cell.Atom(machine.Symbols.True) : TermReifier.ToHeap(machine, body, variables);

            var term = new TermBuffer();
            Cell clause = machine.CreateStructure(rule, [headCell, bodyCell]);
            int root = term.Copy(machine, DatabaseBuiltins.NormalizeClause(machine, clause));

            predicate.Append(
                new DynamicClause
                {
                    CodeAddress = address,
                    Term = term,
                    TermRoot = root,
                    Birth = _program.Generation,
                }
            );
        }
    }

    private static void Report(List<Diagnostic> diagnostics, string id, string message, SourceSpan span, string? fileName) =>
        diagnostics.Add(new Diagnostic(id, DiagnosticSeverity.Error, message, span, fileName));

    private int CompileDirective(SyntaxTerm goal, List<Diagnostic> diagnostics, out bool deferred, string? fileName)
    {
        deferred = goal is CompoundTerm { Name: "initialization", Arity: 1 };
        SyntaxTerm actual = deferred ? ((CompoundTerm)goal).Arguments[0] : goal;

        var compiler = new ClauseCompiler(_program, _constants, diagnostics, fileName);
        return compiler.Compile(new AtomTerm("$directive", actual.Span), actual);
    }

    private void EmitPredicate(
        int functorId,
        IReadOnlyList<(SyntaxTerm Head, SyntaxTerm? Body)> clauses,
        List<Diagnostic> diagnostics,
        string? fileName
    )
    {
        int entry = _program.CodeLength;
        int pendingAlternative = -1;

        for (int i = 0; i < clauses.Count; i++)
        {
            if (clauses.Count > 1)
            {
                if (i == 0)
                {
                    pendingAlternative = _program.Emit(OpCode.TryMeElse, 0) + 1;
                }
                else if (i < clauses.Count - 1)
                {
                    _program.Patch(pendingAlternative, _program.CodeLength);
                    pendingAlternative = _program.Emit(OpCode.RetryMeElse, 0) + 1;
                }
                else
                {
                    _program.Patch(pendingAlternative, _program.CodeLength);
                    _program.Emit(OpCode.TrustMe);
                }
            }

            var compiler = new ClauseCompiler(_program, _constants, diagnostics, fileName);
            compiler.Compile(clauses[i].Head, clauses[i].Body);
        }

        _program.DefinePredicate(functorId, entry, _userPredicates);
    }

    private bool TryGetHeadFunctor(SyntaxTerm head, out int functorId)
    {
        switch (head)
        {
            case AtomTerm atom:
                functorId = _program.Symbols.InternFunctor(atom.Name, 0);
                return true;

            case CompoundTerm compound:
                functorId = _program.Symbols.InternFunctor(compound.Name, compound.Arity);
                return true;

            default:
                functorId = -1;
                return false;
        }
    }

    private PredicateIndicator SourceIndicatorOf(string module, int functorId)
    {
        Functor functor = _program.Symbols.GetFunctor(functorId);
        string compiledName = _program.Symbols.AtomName(functor.NameAtom);
        string sourceName =
            module == ModuleTable.UserModule ? compiledName : compiledName[(module.Length + 1)..];
        return new PredicateIndicator(sourceName, functor.Arity);
    }
}
