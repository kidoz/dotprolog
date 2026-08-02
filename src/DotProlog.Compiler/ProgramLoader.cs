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
        _modules = modules ?? new ModuleTable(program.Modules);
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

        // double_quotes belongs to the load unit. A directive still changes how the rest of this file
        // reads, but the entering value is restored afterwards, so a library that declares its own
        // convention cannot silently change how whatever is consulted next is read.
        bool isoModuleText = ContainsIsoModuleMarker(clauses);
        DoubleQuotesMode entering = _program.Flags.DoubleQuotes;
        OperatorTable? enteringOperators = isoModuleText ? _program.Operators.Copy() : null;
        CharacterConversionTable? enteringConversions = isoModuleText ? _program.CharacterConversions.Copy() : null;
        PrologFlags? enteringFlags = isoModuleText ? _program.Flags.Copy() : null;
        try
        {
            return isoModuleText
                ? LoadIsoModuleText(clauses, fileName, directiveExecutor)
                : LoadScoped(clauses, fileName, directiveExecutor);
        }
        finally
        {
            if (isoModuleText)
            {
                _program.Operators.ReplaceWith(enteringOperators!);
                _program.CharacterConversions.ReplaceWith(enteringConversions!);
                _program.Flags.ReplaceWith(enteringFlags!);
            }
            else
            {
                _program.Flags.DoubleQuotes = entering;
            }
        }
    }

    private LoadResult LoadScoped(
        IReadOnlyList<SyntaxTerm> clauses,
        string? fileName,
        Func<int, RunResult>? directiveExecutor,
        string? forcedModule = null
    )
    {
        List<Diagnostic> diagnostics = [];
        List<int> directives = [];
        List<int> initialization = [];

        // Reading happens in two passes. The first settles which module the unit belongs to and what
        // it defines; only then can a call in a body be told from one to somewhere else.
        var unit = new LoadUnit { Module = forcedModule ?? ModuleTable.UserModule };
        Collect(clauses, unit, diagnostics, fileName);
        DeclareMultifilePredicates(unit, diagnostics, fileName);

        var resolver = new ModuleResolver(_modules, unit.Module, unit.Defines, _program, isoContext: forcedModule is not null);
        HashSet<int> unitDefinitions =
        [
            .. unit.Defines.Select(indicator =>
                _program.Symbols.InternFunctor(ModuleTable.QualifiedName(unit.Module, indicator.Name), indicator.Arity)
            ),
        ];
        HashSet<int> dynamicPredicates = DeclareDynamicPredicates(unit, resolver, diagnostics, fileName);
        Dictionary<int, List<(SyntaxTerm Head, SyntaxTerm? Body)>> accumulated = [];
        Dictionary<int, List<(SyntaxTerm Head, SyntaxTerm? Body)>> pending = [];
        Dictionary<int, List<(SyntaxTerm Head, SyntaxTerm? Body)>> storedPending = [];
        List<int> dirtyOrder = [];
        HashSet<int> dirty = [];
        var halted = false;

        foreach (LoadItem item in unit.Items)
        {
            if (item is ClauseItem clause)
            {
                SyntaxTerm resolvedHead = resolver.ResolveHead(clause.Head);
                SyntaxTerm? resolvedBody = clause.Body is null ? null : resolver.ResolveGoal(clause.Body);

                if (!TryGetHeadFunctor(resolvedHead, out var functorId))
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

                if (forcedModule is not null && !dynamicPredicates.Contains(functorId) && !_program.IsDynamic(functorId))
                {
                    RetainIsoStaticClause(forcedModule, resolvedHead, clause.Body);
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
                if (!storedPending.TryGetValue(functorId, out List<(SyntaxTerm, SyntaxTerm?)>? storedClauses))
                {
                    storedClauses = [];
                    storedPending[functorId] = storedClauses;
                }

                storedClauses.Add((resolvedHead, forcedModule is null ? resolvedBody : clause.Body));
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

            SyntaxTerm resolvedDirective =
                forcedModule is not null && goal is CompoundTerm { Name: "initialization", Arity: 1 } initializationGoal
                    ? initializationGoal with
                    {
                        Arguments = [resolver.ResolveGoal(initializationGoal.Arguments[0])],
                    }
                    : resolver.ResolveGoal(goal);
            var address = CompileDirective(resolvedDirective, diagnostics, out var deferred, fileName, unitDefinitions);
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
            foreach (var functorId in dirtyOrder)
            {
                if (dynamicPredicates.Contains(functorId) || _program.IsDynamic(functorId))
                {
                    EmitDynamicClauses(
                        functorId,
                        pending[functorId],
                        storedPending[functorId],
                        diagnostics,
                        fileName,
                        unitDefinitions
                    );
                    continue;
                }

                PredicateIndicator sourceIndicator = SourceIndicatorOf(unit.Module, functorId);
                if (_modules.IsMultifile(unit.Module, sourceIndicator))
                {
                    IReadOnlyList<(SyntaxTerm Head, SyntaxTerm? Body)> multifileClauses = _modules.AppendMultifileClauses(
                        unit.Module,
                        sourceIndicator,
                        pending[functorId]
                    );
                    EmitPredicate(functorId, multifileClauses, diagnostics, fileName, unitDefinitions);
                    continue;
                }

                EmitPredicate(functorId, accumulated[functorId], diagnostics, fileName, unitDefinitions);
            }

            pending.Clear();
            storedPending.Clear();
            dirty.Clear();
            dirtyOrder.Clear();
        }
    }

    private static bool ContainsIsoModuleMarker(IReadOnlyList<SyntaxTerm> clauses) =>
        clauses.Any(clause =>
            clause is CompoundTerm { Name: ":-", Arity: 1, Arguments: [CompoundTerm marker] }
            && marker.Name is "module" or "end_module" or "body" or "end_body"
            && marker.Arity == 1
        );

    private void RetainIsoStaticClause(string module, SyntaxTerm head, SyntaxTerm? body)
    {
        Machine machine = _machine!;
        Dictionary<string, Cell> variables = [];
        Cell headCell = TermReifier.ToHeap(machine, head, variables);
        Cell bodyCell = body is null ? Cell.Atom(machine.Symbols.True) : TermReifier.ToHeap(machine, body, variables);
        Cell clause = machine.CreateStructure(machine.Symbols.InternFunctor(":-", 2), [headCell, bodyCell]);
        var term = new TermBuffer();
        var root = term.Copy(machine, DatabaseBuiltins.NormalizeClause(machine, clause));

        string compiledName;
        int arity;
        if (headCell.Tag == CellTag.Atom)
        {
            compiledName = machine.Symbols.AtomName(headCell.Index);
            arity = 0;
        }
        else
        {
            Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(headCell.Index).Index);
            compiledName = machine.Symbols.AtomName(functor.NameAtom);
            arity = functor.Arity;
        }

        var separator = compiledName.LastIndexOf(':');
        string name = separator >= 0 ? compiledName[(separator + 1)..] : compiledName;
        ModulePredicateDefinition metadata = _modules
            .Catalog.Declare(module)
            .Predicate(new ModulePredicateIndicator(name, arity));
        metadata.AddStaticClause(term.Cells, root);
    }

    /// <summary>Prepares an ISO/IEC 13211-2 module text containing interfaces followed by bodies.</summary>
    private LoadResult LoadIsoModuleText(
        IReadOnlyList<SyntaxTerm> clauses,
        string? fileName,
        Func<int, RunResult>? directiveExecutor
    )
    {
        List<Diagnostic> diagnostics = [];
        List<IsoInterface> interfaces = [];
        List<IsoBody> bodies = [];
        IsoInterface? currentInterface = null;
        IsoBody? currentBody = null;
        Stack<IsoBody> enclosingBodies = [];
        var currentBodyIsImplicitUser = false;
        var userTextSeen = false;

        foreach (SyntaxTerm clause in clauses)
        {
            if (clause is CompoundTerm { Name: ":-", Arity: 1, Arguments: [CompoundTerm marker] })
            {
                if (marker is { Name: "module", Arity: 1 })
                {
                    if (
                        currentInterface is not null
                        || currentBody is not null && !currentBodyIsImplicitUser
                        || marker.Arguments[0] is not AtomTerm name
                    )
                    {
                        ReportInvalidIsoModuleText(
                            diagnostics,
                            "module/1 must start a named interface outside another interface or explicit body.",
                            marker,
                            fileName
                        );
                        continue;
                    }

                    currentBody = null;
                    currentBodyIsImplicitUser = false;
                    if (
                        (name.Name == ModuleTable.UserModule && userTextSeen)
                        || (_modules.Catalog.TryGet(name.Name, out ModuleDefinition? existing) && existing!.InterfacePrepared)
                    )
                    {
                        ReportInvalidIsoModuleText(
                            diagnostics,
                            $"The interface for module {name.Name} has already been prepared or follows its body.",
                            marker,
                            fileName
                        );
                    }

                    currentInterface = new IsoInterface(name.Name, marker.Span);
                    interfaces.Add(currentInterface);
                    _modules.Catalog.Declare(name.Name);
                    continue;
                }

                if (marker is { Name: "end_module", Arity: 1 })
                {
                    if (
                        currentInterface is null
                        || marker.Arguments[0] is not AtomTerm name
                        || name.Name != currentInterface.Module
                    )
                    {
                        ReportInvalidIsoModuleText(
                            diagnostics,
                            "end_module/1 must match the open module interface.",
                            marker,
                            fileName
                        );
                        continue;
                    }

                    _modules.Catalog.Declare(currentInterface.Module).InterfacePrepared = true;
                    currentInterface = null;
                    continue;
                }

                if (marker is { Name: "body", Arity: 1 })
                {
                    if (currentBodyIsImplicitUser)
                    {
                        currentBody = null;
                        currentBodyIsImplicitUser = false;
                    }

                    if (
                        currentInterface is not null
                        || currentBody is not null and { Module: not ModuleTable.UserModule }
                        || marker.Arguments[0] is not AtomTerm name
                    )
                    {
                        ReportInvalidIsoModuleText(
                            diagnostics,
                            "body/1 must start a named body outside an interface or body.",
                            marker,
                            fileName
                        );
                        continue;
                    }

                    if (
                        name.Name != ModuleTable.UserModule
                        && (
                            !_modules.Catalog.TryGet(name.Name, out ModuleDefinition? definition)
                            || !definition!.InterfacePrepared
                        )
                    )
                    {
                        ReportInvalidIsoModuleText(
                            diagnostics,
                            $"Module {name.Name} has no prepared interface.",
                            marker,
                            fileName
                        );
                    }

                    if (currentBody is not null)
                    {
                        enclosingBodies.Push(currentBody);
                    }

                    currentBody = new IsoBody(name.Name, marker.Span);
                    bodies.Add(currentBody);
                    currentBodyIsImplicitUser = false;
                    continue;
                }

                if (marker is { Name: "end_body", Arity: 1 })
                {
                    if (currentBody is null || marker.Arguments[0] is not AtomTerm name || name.Name != currentBody.Module)
                    {
                        ReportInvalidIsoModuleText(diagnostics, "end_body/1 must match the open module body.", marker, fileName);
                        continue;
                    }

                    if (enclosingBodies.TryPop(out IsoBody? enclosing))
                    {
                        currentBody = new IsoBody(enclosing.Module, enclosing.Span);
                        bodies.Add(currentBody);
                        currentBodyIsImplicitUser = false;
                    }
                    else
                    {
                        currentBody = null;
                        currentBodyIsImplicitUser = false;
                    }
                    continue;
                }
            }

            if (currentInterface is not null)
            {
                currentInterface.Directives.Add(clause);
            }
            else if (currentBody is not null)
            {
                currentBody.Clauses.Add(clause);
            }
            else
            {
                currentBody = new IsoBody(ModuleTable.UserModule, clause.Span);
                bodies.Add(currentBody);
                currentBodyIsImplicitUser = true;
                currentBody.Clauses.Add(clause);
                userTextSeen = true;
            }
        }

        if (currentInterface is not null)
        {
            ReportInvalidIsoModuleText(
                diagnostics,
                $"The interface for {currentInterface.Module} has no matching end_module/1.",
                currentInterface.Span,
                fileName
            );
        }

        if (currentBody is not null && !currentBodyIsImplicitUser)
        {
            ReportInvalidIsoModuleText(
                diagnostics,
                $"The body for {currentBody.Module} has no matching end_body/1.",
                currentBody.Span,
                fileName
            );
        }

        foreach (IsoInterface moduleInterface in interfaces)
        {
            PrepareIsoInterface(moduleInterface, diagnostics, fileName);
        }

        foreach (IsoBody body in bodies)
        {
            PredeclareIsoBody(body, diagnostics, fileName);
        }

        List<int> directives = [];
        List<int> initialization = [];
        foreach (IsoBody body in bodies)
        {
            ModuleDefinition definition = _modules.Catalog.Declare(body.Module);
            if (definition.TryTakePreparedBody(out ModuleReaderState? state))
            {
                _program.Operators.ReplaceWith(state!.Operators);
                _program.CharacterConversions.ReplaceWith(state.CharacterConversions);
                _program.Flags.ReplaceWith(state.Flags);
            }
            else
            {
                _program.Operators.ReplaceWith(definition.Operators);
                _program.CharacterConversions.ReplaceWith(definition.CharacterConversions);
                _program.Flags.ReplaceWith(definition.Flags);
            }

            List<SyntaxTerm> executable = PrepareIsoBody(body, diagnostics, fileName);
            LoadResult result = LoadScoped(executable, fileName, directiveExecutor, body.Module);
            definition.SeedReaderState(_program.Operators, _program.CharacterConversions, _program.Flags);
            diagnostics.AddRange(result.Diagnostics);
            directives.AddRange(result.DirectiveAddresses);
            initialization.AddRange(result.InitializationAddresses);
            Module = body.Module;
        }

        return new LoadResult(diagnostics, directives, initialization);
    }

    private void PrepareIsoInterface(IsoInterface moduleInterface, List<Diagnostic> diagnostics, string? fileName)
    {
        foreach (SyntaxTerm clause in moduleInterface.Directives)
        {
            if (clause is not CompoundTerm { Name: ":-", Arity: 1 } directive)
            {
                ReportInvalidIsoModuleText(diagnostics, "A module interface may contain directives only.", clause, fileName);
                continue;
            }

            SyntaxTerm goal = directive.Arguments[0];
            switch (goal)
            {
                case CompoundTerm { Name: "export", Arity: 1 } export:
                    DeclareIsoExports(moduleInterface.Module, export.Arguments[0], diagnostics, fileName);
                    break;

                case CompoundTerm { Name: "reexport", Arity: 2 } reexport:
                    ReexportSelected(moduleInterface.Module, reexport, diagnostics, fileName);
                    break;

                case CompoundTerm { Name: "reexport", Arity: 1 } reexport:
                    foreach (SyntaxTerm item in Indicators(reexport.Arguments[0]))
                    {
                        if (item is AtomTerm source)
                        {
                            ReexportAll(moduleInterface.Module, source.Name, reexport, diagnostics, fileName);
                        }
                        else
                        {
                            ReportInvalidIsoModuleText(
                                diagnostics,
                                "reexport/1 expects a module atom or a list or sequence of module atoms.",
                                item,
                                fileName
                            );
                        }
                    }

                    break;

                case CompoundTerm { Name: "metapredicate", Arity: 1 } meta:
                    DeclareIsoMeta(moduleInterface.Module, meta.Arguments[0], diagnostics, fileName);
                    break;

                case CompoundTerm readerDirective
                    when readerDirective
                        is { Name: "op", Arity: 3 }
                            or { Name: "char_conversion", Arity: 2 }
                            or { Name: "set_prolog_flag", Arity: 2 }:
                    // These directives have already affected lexical preparation at their source
                    // position. Module-specific reader snapshots are installed by the reader seam.
                    ValidateIsoReaderDirective(readerDirective, diagnostics, fileName);
                    break;

                default:
                    ReportInvalidIsoModuleText(
                        diagnostics,
                        "This directive is not permitted in a module interface.",
                        goal,
                        fileName
                    );
                    break;
            }
        }
    }

    private void PredeclareIsoBody(IsoBody body, List<Diagnostic> diagnostics, string? fileName)
    {
        foreach (SyntaxTerm clause in body.Clauses)
        {
            SyntaxTerm candidate = clause;
            if (clause is CompoundTerm { Name: ":-", Arity: 2 } rule)
            {
                candidate = rule.Arguments[0];
            }
            else if (clause is CompoundTerm { Name: "-->", Arity: 2 } grammar)
            {
                candidate = grammar.Arguments[0] is CompoundTerm { Name: ",", Arity: 2 } semicontext
                    ? semicontext.Arguments[0]
                    : grammar.Arguments[0];
                switch (candidate)
                {
                    case AtomTerm atom:
                        DefineIsoBodyPredicate(
                            body.Module,
                            new PredicateIndicator(atom.Name, 2),
                            candidate,
                            diagnostics,
                            fileName
                        );
                        break;
                    case CompoundTerm compound:
                        DefineIsoBodyPredicate(
                            body.Module,
                            new PredicateIndicator(compound.Name, compound.Arity + 2),
                            candidate,
                            diagnostics,
                            fileName
                        );
                        break;
                }

                continue;
            }
            else if (
                clause is CompoundTerm { Name: ":-", Arity: 1, Arguments: [CompoundTerm declaration] }
                && declaration.Name is "dynamic" or "multifile" or "discontiguous"
                && declaration.Arity == 1
            )
            {
                foreach (SyntaxTerm item in Indicators(declaration.Arguments[0]))
                {
                    if (TryIndicator(item, out PredicateIndicator indicator))
                    {
                        DefineIsoBodyPredicate(body.Module, indicator, item, diagnostics, fileName);
                    }
                }

                continue;
            }

            switch (candidate)
            {
                case AtomTerm atom:
                    if (IsPredefinedProcedure(new PredicateIndicator(atom.Name, 0)))
                    {
                        ReportInvalidIsoModuleText(
                            diagnostics,
                            "A module clause cannot define a predefined procedure.",
                            atom,
                            fileName
                        );
                        break;
                    }

                    DefineIsoBodyPredicate(body.Module, new PredicateIndicator(atom.Name, 0), atom, diagnostics, fileName);
                    break;
                case CompoundTerm { Name: ":", Arity: 2 } qualified:
                    ReportInvalidIsoModuleText(diagnostics, "A module clause head must be unqualified.", qualified, fileName);
                    break;
                case CompoundTerm compound when compound.Name != ":-":
                    if (IsPredefinedProcedure(new PredicateIndicator(compound.Name, compound.Arity)))
                    {
                        ReportInvalidIsoModuleText(
                            diagnostics,
                            "A module clause cannot define a predefined procedure.",
                            compound,
                            fileName
                        );
                        break;
                    }

                    DefineIsoBodyPredicate(
                        body.Module,
                        new PredicateIndicator(compound.Name, compound.Arity),
                        compound,
                        diagnostics,
                        fileName
                    );
                    break;
            }
        }
    }

    private void DefineIsoBodyPredicate(
        string module,
        PredicateIndicator indicator,
        SyntaxTerm location,
        List<Diagnostic> diagnostics,
        string? fileName
    )
    {
        if (
            _modules.Catalog.TryGet(module, out ModuleDefinition? definition)
            && definition!.Imports.ContainsKey(new ModulePredicateIndicator(indicator.Name, indicator.Arity))
        )
        {
            ReportInvalidIsoModuleText(
                diagnostics,
                $"{indicator} is imported or re-exported and cannot also be defined in {module}.",
                location,
                fileName
            );
        }

        _modules.Define(module, indicator);
    }

    private List<SyntaxTerm> PrepareIsoBody(IsoBody body, List<Diagnostic> diagnostics, string? fileName)
    {
        List<SyntaxTerm> executable = [];
        foreach (SyntaxTerm clause in body.Clauses)
        {
            if (clause is CompoundTerm { Name: ":-", Arity: 1, Arguments: [CompoundTerm import] })
            {
                if (import is { Name: "import", Arity: 2 } && import.Arguments[0] is AtomTerm source)
                {
                    ImportSelected(body.Module, source.Name, import.Arguments[1], import, diagnostics, fileName);
                    continue;
                }

                if (import is { Name: "import", Arity: 1 })
                {
                    foreach (SyntaxTerm item in Indicators(import.Arguments[0]))
                    {
                        if (item is AtomTerm sourceModule)
                        {
                            ImportAll(body.Module, sourceModule.Name, import, diagnostics, fileName);
                        }
                        else
                        {
                            ReportInvalidIsoModuleText(
                                diagnostics,
                                "import/1 expects a module atom or a list or sequence of module atoms.",
                                item,
                                fileName
                            );
                        }
                    }

                    continue;
                }
            }

            executable.Add(clause);
        }

        return executable;
    }

    private void DeclareIsoExports(string module, SyntaxTerm declarations, List<Diagnostic> diagnostics, string? fileName)
    {
        List<PredicateIndicator> exports = [];
        foreach (SyntaxTerm item in Indicators(declarations))
        {
            if (TryIndicator(item, out PredicateIndicator indicator))
            {
                if (IsPredefinedProcedure(indicator))
                {
                    ReportInvalidIsoModuleText(
                        diagnostics,
                        $"A control construct or built-in procedure cannot be exported as {indicator}.",
                        item,
                        fileName
                    );
                    continue;
                }

                exports.Add(indicator);
            }
            else
            {
                ReportInvalidIsoModuleText(diagnostics, "export/1 expects predicate indicators.", item, fileName);
            }
        }

        _modules.Declare(module, exports);
    }

    private void ImportSelected(
        string importer,
        string source,
        SyntaxTerm declarations,
        SyntaxTerm location,
        List<Diagnostic> diagnostics,
        string? fileName
    )
    {
        if (!_modules.Catalog.Contains(source))
        {
            ReportInvalidIsoModuleText(diagnostics, $"Module {source} does not exist.", location, fileName);
            return;
        }

        foreach (SyntaxTerm item in Indicators(declarations))
        {
            if (!TryIndicator(item, out PredicateIndicator indicator))
            {
                ReportInvalidIsoModuleText(diagnostics, "An import must be a predicate indicator.", item, fileName);
                continue;
            }

            if (!_modules.Exports(source, indicator))
            {
                ReportInvalidIsoModuleText(diagnostics, $"Module {source} does not export {indicator}.", item, fileName);
                continue;
            }

            if (!_modules.TryImport(importer, indicator, source, out string? conflict))
            {
                ReportInvalidIsoModuleText(
                    diagnostics,
                    $"{indicator} is already visible in {importer} from {conflict}.",
                    item,
                    fileName
                );
            }
        }
    }

    private void ImportAll(string importer, string source, SyntaxTerm location, List<Diagnostic> diagnostics, string? fileName)
    {
        if (!_modules.Catalog.Contains(source))
        {
            ReportInvalidIsoModuleText(diagnostics, $"Module {source} does not exist.", location, fileName);
            return;
        }

        foreach (PredicateIndicator indicator in _modules.ExportsOf(source))
        {
            if (!_modules.TryImport(importer, indicator, source, out string? conflict))
            {
                ReportInvalidIsoModuleText(
                    diagnostics,
                    $"{indicator} is already visible in {importer} from {conflict}.",
                    location,
                    fileName
                );
            }
        }
    }

    private void ReexportSelected(string module, CompoundTerm declaration, List<Diagnostic> diagnostics, string? fileName)
    {
        if (declaration.Arguments[0] is not AtomTerm source)
        {
            ReportInvalidIsoModuleText(diagnostics, "reexport/2 expects a module atom.", declaration, fileName);
            return;
        }

        ImportSelected(module, source.Name, declaration.Arguments[1], declaration, diagnostics, fileName);
        DeclareIsoExports(module, declaration.Arguments[1], diagnostics, fileName);
    }

    private void ReexportAll(string module, string source, SyntaxTerm location, List<Diagnostic> diagnostics, string? fileName)
    {
        ImportAll(module, source, location, diagnostics, fileName);
        _modules.Declare(module, _modules.ExportsOf(source));
    }

    private void DeclareIsoMeta(string module, SyntaxTerm declarations, List<Diagnostic> diagnostics, string? fileName)
    {
        foreach (SyntaxTerm item in Indicators(declarations))
        {
            if (item is not CompoundTerm specification)
            {
                ReportInvalidIsoModuleText(diagnostics, "metapredicate/1 expects a compound template.", item, fileName);
                continue;
            }

            List<(int Position, int Extra)> meta = [];
            var valid = true;
            var template = new char[specification.Arity];
            for (var index = 0; index < specification.Arity; index++)
            {
                if (specification.Arguments[index] is not AtomTerm mode || mode.Name is not (":" or "*"))
                {
                    ReportInvalidIsoModuleText(
                        diagnostics,
                        "ISO metapredicate modes are ':' or '*'.",
                        specification.Arguments[index],
                        fileName
                    );
                    valid = false;
                    continue;
                }

                template[index] = mode.Name[0];
                if (mode.Name == ":")
                {
                    meta.Add((index, 0));
                }
            }

            if (valid)
            {
                var indicator = new PredicateIndicator(specification.Name, specification.Arity);
                if (IsPredefinedProcedure(indicator))
                {
                    ReportInvalidIsoModuleText(
                        diagnostics,
                        $"A control construct or built-in procedure cannot be declared as {indicator}.",
                        specification,
                        fileName
                    );
                    continue;
                }

                _modules.DeclareMeta(module, specification.Name, specification.Arity, meta, new string(template));
                _modules.Declare(module, [indicator]);
            }
        }
    }

    private static void ValidateIsoReaderDirective(CompoundTerm directive, List<Diagnostic> diagnostics, string? fileName)
    {
        bool valid = directive switch
        {
            { Name: "op", Arguments: [IntegerTerm priority, AtomTerm type, var names] } => priority.Value is >= 0 and <= 1200
                && type.Name is "xfx" or "xfy" or "yfx" or "fx" or "fy" or "xf" or "yf"
                && OperatorNames(names).All(name => name is AtomTerm),
            { Name: "char_conversion", Arguments: [AtomTerm input, AtomTerm output] } => IsSingleCharacter(input)
                && IsSingleCharacter(output),
            { Name: "set_prolog_flag", Arguments: [AtomTerm flag, AtomTerm value] } => (flag.Name, value.Name) switch
            {
                ("char_conversion", "on" or "off") => true,
                ("debug", "on" or "off") => true,
                ("double_quotes", "codes" or "chars" or "atom") => true,
                ("unknown", "error" or "warning" or "fail") => true,
                _ => false,
            },
            _ => false,
        };

        if (!valid)
        {
            ReportInvalidIsoModuleText(
                diagnostics,
                $"{directive.Name}/{directive.Arity} does not satisfy the corresponding built-in predicate constraints.",
                directive,
                fileName
            );
        }
    }

    private static IEnumerable<SyntaxTerm> OperatorNames(SyntaxTerm term)
    {
        SyntaxTerm current = term;
        while (current is CompoundTerm { Name: ".", Arity: 2 } cons)
        {
            yield return cons.Arguments[0];
            current = cons.Arguments[1];
        }

        if (current is not AtomTerm { Name: "[]" })
        {
            yield return current;
        }
    }

    private static bool IsSingleCharacter(AtomTerm atom) => atom.Name.Length == 1;

    private bool IsPredefinedProcedure(PredicateIndicator indicator)
    {
        if (
            (indicator.Name is "," or ";" or "->" or "*->" or ":" && indicator.Arity == 2)
            || (indicator.Name == "\\+" && indicator.Arity == 1)
            || (indicator.Name == "!" && indicator.Arity == 0)
        )
        {
            return true;
        }

        var functor = _program.Symbols.InternFunctor(indicator.Name, indicator.Arity);
        return _program.Builtins.TryGetId(functor, out _) || (_program.IsDefined(functor) && !_program.IsUserPredicate(functor));
    }

    private static void ReportInvalidIsoModuleText(
        List<Diagnostic> diagnostics,
        string message,
        SyntaxTerm location,
        string? fileName
    ) => Report(diagnostics, CompilerDiagnosticIds.InvalidIsoModuleText, message, location.Span, fileName);

    private static void ReportInvalidIsoModuleText(
        List<Diagnostic> diagnostics,
        string message,
        SourceSpan span,
        string? fileName
    ) => Report(diagnostics, CompilerDiagnosticIds.InvalidIsoModuleText, message, span, fileName);

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

            var qualified = _program.Symbols.InternFunctor(ModuleTable.QualifiedName(unit.Module, export.Name), export.Arity);

            _program.AliasPredicate(_program.Symbols.InternFunctor(export.Name, export.Arity), qualified);
        }
    }

    /// <summary>Runs the first pass: declarations, and the set of predicates the unit defines.</summary>
    private void Collect(IReadOnlyList<SyntaxTerm> clauses, LoadUnit unit, List<Diagnostic> diagnostics, string? fileName)
    {
        DoubleQuotesMode doubleQuotes = _program.Flags.DoubleQuotes;
        var firstTerm = true;

        foreach (SyntaxTerm rawClause in clauses)
        {
            SyntaxTerm clause = TermNormalizer.Normalize(rawClause, doubleQuotes);
            var isFirstTerm = firstTerm;
            firstTerm = false;

            if (clause is CompoundTerm { Name: ":-", Arity: 1 } directive)
            {
                SyntaxTerm goal = directive.Arguments[0];

                switch (goal)
                {
                    case CompoundTerm { Name: "module", Arity: 2 } declaration:
                        if (_program.LanguageMode == PrologLanguageMode.StrictIso)
                        {
                            Report(
                                diagnostics,
                                CompilerDiagnosticIds.StrictIsoViolation,
                                "module/2 is a compatibility extension; strict ISO mode uses module/1 and end_module/1.",
                                declaration.Span,
                                fileName
                            );
                            continue;
                        }

                        if (!isFirstTerm || unit.HasModuleDeclaration)
                        {
                            Report(
                                diagnostics,
                                CompilerDiagnosticIds.InvalidModuleDeclaration,
                                "module/2 must be the first term and may occur only once in a module text.",
                                declaration.Span,
                                fileName
                            );
                            continue;
                        }

                        unit.HasModuleDeclaration = true;
                        DeclareModule(declaration, unit, diagnostics, fileName);
                        continue;

                    case CompoundTerm { Name: "use_module", Arity: 1 or 2 } import:
                        if (_program.LanguageMode == PrologLanguageMode.StrictIso)
                        {
                            Report(
                                diagnostics,
                                CompilerDiagnosticIds.StrictIsoViolation,
                                "use_module is a compatibility extension; strict ISO mode uses import/1,2 in a body.",
                                import.Span,
                                fileName
                            );
                            continue;
                        }

                        UseModule(import, unit, diagnostics, fileName);
                        continue;

                    case CompoundTerm { Name: "meta_predicate", Arity: 1 } meta:
                        if (_program.LanguageMode == PrologLanguageMode.StrictIso)
                        {
                            Report(
                                diagnostics,
                                CompilerDiagnosticIds.StrictIsoViolation,
                                "meta_predicate/1 is a compatibility extension; strict ISO mode uses metapredicate/1.",
                                meta.Span,
                                fileName
                            );
                            continue;
                        }

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
                if (
                    GrammarHeadIsReserved(
                        grammarRule.Arguments[0],
                        _program.LanguageMode == PrologLanguageMode.StrictIso,
                        out SyntaxTerm culprit
                    )
                )
                {
                    Report(
                        diagnostics,
                        CompilerDiagnosticIds.InvalidGrammarRule,
                        "A grammar rule head may not be a grammar control construct.",
                        culprit.Span,
                        fileName
                    );
                    continue;
                }

                if (
                    !DcgTranslator.TryTranslate(
                        grammarRule,
                        diagnostics,
                        fileName,
                        _program.LanguageMode,
                        out head,
                        out SyntaxTerm translated
                    )
                )
                {
                    continue;
                }

                if (GrammarHeadCollidesWithPredefinedProcedure(head))
                {
                    Report(
                        diagnostics,
                        CompilerDiagnosticIds.InvalidGrammarRule,
                        "A grammar rule may not expand to a predefined procedure.",
                        grammarRule.Arguments[0].Span,
                        fileName
                    );
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

        foreach (PredicateIndicator predicate in unit.Defines)
        {
            _modules.Define(unit.Module, predicate);
        }
    }

    private bool GrammarHeadCollidesWithPredefinedProcedure(SyntaxTerm head)
    {
        string name;
        int arity;
        switch (head)
        {
            case AtomTerm atom:
                name = atom.Name;
                arity = 0;
                break;

            case CompoundTerm compound:
                name = compound.Name;
                arity = compound.Arity;
                break;

            default:
                return false;
        }

        var functor = _program.Symbols.InternFunctor(name, arity);
        return _program.Builtins.TryGetId(functor, out _) || (_program.IsDefined(functor) && !_program.IsUserPredicate(functor));
    }

    private static bool GrammarHeadIsReserved(SyntaxTerm head, bool strictIso, out SyntaxTerm culprit)
    {
        culprit = head;
        SyntaxTerm nonTerminal = head is CompoundTerm { Name: ",", Arity: 2 } semicontext ? semicontext.Arguments[0] : head;
        culprit = nonTerminal;

        return nonTerminal switch
        {
            AtomTerm { Name: "[]" or "!" } => true,
            CompoundTerm { Name: "," or ";" or "|" or "->", Arity: 2 } => true,
            CompoundTerm { Name: "*->", Arity: 2 } => !strictIso,
            CompoundTerm { Name: "\\+" or "{}" or "call" or "phrase", Arity: 1 } => true,
            CompoundTerm { Name: ".", Arity: 2 } => true,
            _ => false,
        };
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

        var path = ResolvePath(file.Name, fileName);
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
            Report(diagnostics, CompilerDiagnosticIds.EnsureLoadedNotFound, exception.Message, file.Span, fileName);
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

        var path = ResolvePath(file.Name, fileName);
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

        var loaded = _modules.LoadedModuleOf(path);
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
                    continue;
                }

                Report(
                    diagnostics,
                    CompilerDiagnosticIds.InvalidModuleImport,
                    "A selected import must be a predicate indicator of the form Name/Arity or Name//Arity.",
                    item.Span,
                    fileName
                );
            }

            wanted = listed;
        }

        foreach (PredicateIndicator indicator in wanted)
        {
            if (!_modules.Exports(loaded, indicator))
            {
                Report(
                    diagnostics,
                    CompilerDiagnosticIds.InvalidModuleImport,
                    $"Module {loaded} does not export {indicator}.",
                    import.Span,
                    fileName
                );
                continue;
            }

            if (!_modules.TryImport(unit.Module, indicator, loaded, out var conflictingModule))
            {
                Report(
                    diagnostics,
                    CompilerDiagnosticIds.InvalidModuleImport,
                    $"{indicator} is already imported into module {unit.Module} from module {conflictingModule}.",
                    import.Span,
                    fileName
                );
            }
        }
    }

    /// <summary>Finds the file a <c>use_module</c> names, relative to the file that asked for it.</summary>
    private static string? ResolvePath(string name, string? fileName)
    {
        var directory = fileName is null
            ? Directory.GetCurrentDirectory()
            : (Path.GetDirectoryName(Path.GetFullPath(fileName)) ?? ".");

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
            for (var i = 0; i < spec.Arity; i++)
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

        var compiledArity = arity.Value + (slash.Name == "//" ? 2 : 0);
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

        internal bool HasModuleDeclaration { get; set; }

        internal HashSet<PredicateIndicator> Defines { get; } = [];

        internal List<SyntaxTerm> Dynamic { get; } = [];

        internal List<SyntaxTerm> Multifile { get; } = [];

        internal List<LoadItem> Items { get; } = [];
    }

    private sealed class IsoInterface(string module, SourceSpan span)
    {
        internal string Module { get; } = module;

        internal SourceSpan Span { get; } = span;

        internal List<SyntaxTerm> Directives { get; } = [];
    }

    private sealed class IsoBody(string module, SourceSpan span)
    {
        internal string Module { get; } = module;

        internal SourceSpan Span { get; } = span;

        internal List<SyntaxTerm> Clauses { get; } = [];
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

            var functorId = _program.Symbols.InternFunctor(ModuleTable.QualifiedName(module, indicator.Name), indicator.Arity);
            _modules.DeclareDynamic(module, indicator);
            _program.DeclareDynamic(functorId, _userPredicates);
            declared.Add(functorId);
        }
    }

    /// <summary>Compiles each clause of a dynamic predicate separately and adds it to the database.</summary>
    private void EmitDynamicClauses(
        int functorId,
        List<(SyntaxTerm Head, SyntaxTerm? Body)> clauses,
        List<(SyntaxTerm Head, SyntaxTerm? Body)> storedClauses,
        List<Diagnostic> diagnostics,
        string? fileName,
        IReadOnlySet<int> unitDefinitions
    )
    {
        DynamicPredicate predicate = _program.DeclareDynamic(functorId, _userPredicates);
        Machine machine = _machine!;
        var rule = _program.Symbols.InternFunctor(":-", 2);

        for (var index = 0; index < clauses.Count; index++)
        {
            (SyntaxTerm head, SyntaxTerm? body) = clauses[index];
            var compiler = new ClauseCompiler(
                _program,
                _constants,
                diagnostics,
                fileName,
                unitDefinitions,
                trustedImplementation: !_userPredicates
            );
            var address = compiler.Compile(head, body);
            if (address < 0)
            {
                continue;
            }

            // retract/1 matches against the clause as a term, so keep a detached copy of it.
            Dictionary<string, Cell> variables = [];
            Cell headCell = TermReifier.ToHeap(machine, storedClauses[index].Head, variables);
            Cell bodyCell = storedClauses[index].Body is null
                ? Cell.Atom(machine.Symbols.True)
                : TermReifier.ToHeap(machine, storedClauses[index].Body!, variables);

            var term = new TermBuffer();
            Cell clause = machine.CreateStructure(rule, [headCell, bodyCell]);
            var root = term.Copy(machine, DatabaseBuiltins.NormalizeClause(machine, clause));

            predicate.Append(
                new DynamicClause
                {
                    CodeAddress = address,
                    Term = term,
                    TermRoot = root,
                    Birth = _program.Generation,
                    IndexKey = FirstArgumentSyntaxKey(head),
                }
            );
        }
    }

    private static void Report(List<Diagnostic> diagnostics, string id, string message, SourceSpan span, string? fileName) =>
        diagnostics.Add(new Diagnostic(id, DiagnosticSeverity.Error, message, span, fileName));

    private int CompileDirective(
        SyntaxTerm goal,
        List<Diagnostic> diagnostics,
        out bool deferred,
        string? fileName,
        IReadOnlySet<int> unitDefinitions
    )
    {
        deferred = goal is CompoundTerm { Name: "initialization", Arity: 1 };
        SyntaxTerm actual = deferred ? ((CompoundTerm)goal).Arguments[0] : goal;

        var compiler = new ClauseCompiler(
            _program,
            _constants,
            diagnostics,
            fileName,
            unitDefinitions,
            trustedImplementation: !_userPredicates
        );
        return compiler.Compile(new AtomTerm("$directive", actual.Span), actual);
    }

    private void EmitPredicate(
        int functorId,
        IReadOnlyList<(SyntaxTerm Head, SyntaxTerm? Body)> clauses,
        List<Diagnostic> diagnostics,
        string? fileName,
        IReadOnlySet<int> unitDefinitions
    )
    {
        if (clauses.Count > 1 && _program.EmitFirstArgumentIndexing && _program.Symbols.GetFunctor(functorId).Arity >= 1)
        {
            EmitIndexedPredicate(functorId, clauses, diagnostics, fileName, unitDefinitions);
            return;
        }

        var entry = _program.CodeLength;
        var pendingAlternative = -1;

        for (var i = 0; i < clauses.Count; i++)
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

            var compiler = new ClauseCompiler(
                _program,
                _constants,
                diagnostics,
                fileName,
                unitDefinitions,
                trustedImplementation: !_userPredicates
            );
            compiler.Compile(clauses[i].Head, clauses[i].Body);
        }

        _program.DefinePredicate(functorId, entry, _userPredicates);
    }

    /// <summary>
    /// Emits a multi-clause predicate behind a first-argument clause index: an
    /// <see cref="OpCode.EnterStatic"/> stub, then the clause bodies without try/retry/trust
    /// headers, dispatched through the registered clause table.
    /// </summary>
    private void EmitIndexedPredicate(
        int functorId,
        IReadOnlyList<(SyntaxTerm Head, SyntaxTerm? Body)> clauses,
        List<Diagnostic> diagnostics,
        string? fileName,
        IReadOnlySet<int> unitDefinitions
    )
    {
        var stub = _program.Emit(OpCode.EnterStatic, 0);
        List<int> addresses = new(clauses.Count);
        List<Cell> keys = new(clauses.Count);

        foreach ((SyntaxTerm head, SyntaxTerm? body) in clauses)
        {
            var compiler = new ClauseCompiler(
                _program,
                _constants,
                diagnostics,
                fileName,
                unitDefinitions,
                trustedImplementation: !_userPredicates
            );
            var address = compiler.Compile(head, body);
            if (address < 0)
            {
                continue;
            }

            addresses.Add(address);
            keys.Add(FirstArgumentSyntaxKey(head));
        }

        _program.Patch(stub + 1, _program.AddStaticIndex([.. addresses], [.. keys]));
        _program.DefinePredicate(functorId, stub, _userPredicates);
    }

    /// <summary>
    /// Derives the first-argument index key of a clause head. Anything the mapping does not
    /// recognise keys as matching every call, which is always correct.
    /// </summary>
    private Cell FirstArgumentSyntaxKey(SyntaxTerm head)
    {
        if (TermNormalizer.Normalize(head, _program.Flags.DoubleQuotes) is not CompoundTerm compound || compound.Arity == 0)
        {
            return ClauseIndexing.AnyKey;
        }

        return TermNormalizer.Normalize(compound.Arguments[0], _program.Flags.DoubleQuotes) switch
        {
            AtomTerm atom => Cell.Atom(_program.Symbols.InternAtom(atom.Name)),
            IntegerTerm integer when Cell.FitsInteger(integer.Value) => Cell.Integer60(integer.Value),
            FloatTerm floating => Cell.Float(_program.Symbols.InternFloat(floating.Value)),
            CompoundTerm structure => Cell.Functor(_program.Symbols.InternFunctor(structure.Name, structure.Arity)),
            _ => ClauseIndexing.AnyKey,
        };
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
        var compiledName = _program.Symbols.AtomName(functor.NameAtom);
        var sourceName = module == ModuleTable.UserModule ? compiledName : compiledName[(module.Length + 1)..];
        return new PredicateIndicator(sourceName, functor.Arity);
    }
}
