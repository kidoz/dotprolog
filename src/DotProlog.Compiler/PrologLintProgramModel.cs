using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>A non-executing view of the modules, declarations, and clauses supplied for linting.</summary>
internal sealed class PrologLintProgramModel
{
    private readonly Dictionary<string, string> _moduleByPath;
    private readonly HashSet<string> _unresolvedImportModules = new(StringComparer.Ordinal);
    private readonly HashSet<(string Module, PredicateIndicator Predicate)> _unresolvedImportPredicates = [];
    private readonly HashSet<(string Module, PredicateIndicator Predicate)> _ordinary = [];
    private readonly HashSet<(string Module, PredicateIndicator Predicate)> _nonterminals = [];
    private readonly HashSet<(string Module, PredicateIndicator Predicate)> _dynamicOrMultifile = [];
    private readonly HashSet<string> _effectfulModules = new(StringComparer.Ordinal);

    private PrologLintProgramModel(IReadOnlyList<PrologLintSource> sources)
    {
        Sources = sources;
        _moduleByPath = new Dictionary<string, string>(PathComparer);
        Modules = new ModuleTable();
        Modules.Catalog.Declare(ModuleTable.UserModule);
    }

    internal IReadOnlyList<PrologLintSource> Sources { get; }
    internal bool HasUnresolvedImports => _unresolvedImportModules.Count > 0 || _unresolvedImportPredicates.Count > 0;
    internal ModuleTable Modules { get; }
    internal List<ClauseContext> Clauses { get; } = [];
    internal List<DeclarationSite> Declarations { get; } = [];
    internal List<ImportSite> Imports { get; } = [];
    internal List<(string File, int Offset, string Module, int Context)> ModuleScopes { get; } = [];

    internal static PrologLintProgramModel Build(IReadOnlyList<PrologLintSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var model = new PrologLintProgramModel(sources);
        List<ImportRequest> imports = [];
        foreach (PrologLintSource source in sources)
        {
            model.CollectDefinitions(source, imports);
        }

        foreach (ImportRequest import in imports)
        {
            model.ResolveImport(import);
        }

        var propagated = true;
        while (propagated)
        {
            propagated = false;
            foreach (ImportSite import in model.Imports)
            {
                if (
                    import.SourceModule is { } sourceModule
                    && model._effectfulModules.Contains(sourceModule)
                    && model._effectfulModules.Add(import.Importer)
                )
                {
                    propagated = true;
                }
            }
        }

        return model;
    }

    internal LintResolution Resolve(string module, PredicateIndicator predicate)
    {
        if (!Modules.Catalog.Contains(module))
        {
            return LintResolution.Unknown;
        }

        if (Modules.Defines(module, predicate))
        {
            return LintResolution.Defined(module);
        }

        string? imported = Modules.DefiningModuleOf(module, predicate);
        if (imported is not null)
        {
            return LintResolution.Defined(imported);
        }

        if (module != ModuleTable.UserModule && Modules.Defines(ModuleTable.UserModule, predicate))
        {
            return LintResolution.Defined(ModuleTable.UserModule);
        }

        if (LintBuiltinCatalog.Contains(predicate))
        {
            return LintResolution.Defined(null);
        }

        if (_unresolvedImportModules.Contains(module) || _unresolvedImportPredicates.Contains((module, predicate)))
        {
            return LintResolution.Unknown;
        }

        return LintResolution.Undefined;
    }

    internal bool HasUnresolvedImport(string module, PredicateIndicator predicate) =>
        _unresolvedImportModules.Contains(module) || _unresolvedImportPredicates.Contains((module, predicate));

    internal bool IsOrdinary(string module, PredicateIndicator predicate) =>
        _ordinary.Contains((module, predicate)) || LintBuiltinCatalog.Contains(predicate);

    internal bool IsNonterminal(string module, PredicateIndicator predicate) => _nonterminals.Contains((module, predicate));

    internal bool IsDynamicOrMultifile(string module, PredicateIndicator predicate) =>
        _dynamicOrMultifile.Contains((module, predicate));

    internal bool HasNonPredicateEffects(string module) => _effectfulModules.Contains(module);

    internal int[]? MetaArgumentsOf(string module, PredicateIndicator predicate) => Modules.MetaArgumentsOf(module, predicate);

    internal bool TryStaticClauses(string module, PredicateIndicator predicate, out IReadOnlyList<ClauseContext> clauses)
    {
        clauses = [.. Clauses.Where(clause => clause.Module == module && clause.Indicator == predicate)];
        return clauses.Count > 0 && !_dynamicOrMultifile.Contains((module, predicate));
    }

    private void CollectDefinitions(PrologLintSource source, List<ImportRequest> imports)
    {
        var module = ModuleTable.UserModule;
        CollectModuleScopes(source);
        for (var index = 0; index < source.Clauses.Count; index++)
        {
            SyntaxTerm clause = source.Clauses[index];
            PrologLintSource owner =
                source.FileForClause(index) == source.FileName ? source : source with { FileName = source.FileForClause(index) };
            if (clause is CompoundTerm { Name: ":-", Arity: 1, Arguments: [SyntaxTerm directiveTerm] })
            {
                if (directiveTerm is not CompoundTerm directive)
                {
                    _effectfulModules.Add(module);
                    continue;
                }

                if (directive.Name == "initialization")
                {
                    Clauses.Add(new ClauseContext(owner, clause, module, default, false));
                    _effectfulModules.Add(module);
                }

                module = CollectDirective(owner, directive, module, imports);
                continue;
            }

            var grammar = clause is CompoundTerm { Name: "-->", Arity: 2 };
            SyntaxTerm head = LintSyntax.ClauseHead(clause);
            if (!LintSyntax.TryPredicate(head, grammar, out PredicateIndicator indicator))
            {
                continue;
            }

            Modules.Define(module, indicator);
            if (grammar)
            {
                _nonterminals.Add((module, indicator));
            }
            else
            {
                _ordinary.Add((module, indicator));
            }

            Clauses.Add(new ClauseContext(owner, clause, module, indicator, grammar));
        }
    }

    private void CollectModuleScopes(PrologLintSource source)
    {
        string module = ModuleTable.UserModule;
        Dictionary<string, int> contexts = new(PathComparer);
        IEnumerable<(string FileName, SyntaxTerm? Clause)> order =
            source.SourceOrder
            ?? source.Clauses.Select((clause, index) => (source.FileForClause(index), (SyntaxTerm?)clause)).ToArray();
        foreach (var (file, clause) in order)
        {
            if (!contexts.TryGetValue(file, out int context) || clause is null)
            {
                context = ModuleScopes.Count;
                contexts[file] = context;
                ModuleScopes.Add((file, 0, module, context));
            }
            if (clause is null)
                continue;
            ModuleScopes.Add((file, clause.Span.Start, module, context));
            if (clause is CompoundTerm { Name: ":-", Arity: 1, Arguments: [CompoundTerm directive] })
            {
                module = directive switch
                {
                    { Name: "module", Arity: 1 or 2, Arguments: [AtomTerm name, ..] } => name.Name,
                    { Name: "body", Arity: 1, Arguments: [AtomTerm name] } => name.Name,
                    { Name: "end_module" or "end_body", Arity: 1 } => ModuleTable.UserModule,
                    _ => module,
                };
                ModuleScopes.Add((file, clause.Span.Start + clause.Span.Length, module, context));
            }
        }
    }

    private string CollectDirective(PrologLintSource source, CompoundTerm directive, string module, List<ImportRequest> imports)
    {
        switch (directive.Name, directive.Arity)
        {
            case ("module", 2) when directive.Arguments[0] is AtomTerm modern:
                DeclareModule(modern.Name, directive.Arguments[1], source);
                _moduleByPath[source.FileName] = modern.Name;
                return modern.Name;
            case ("module", 1) when directive.Arguments[0] is AtomTerm iso:
                Modules.Catalog.Declare(iso.Name);
                _moduleByPath[source.FileName] = iso.Name;
                return iso.Name;
            case ("end_module", 1):
                return ModuleTable.UserModule;
            case ("body", 1) when directive.Arguments[0] is AtomTerm body:
                Modules.Catalog.Declare(body.Name);
                return body.Name;
            case ("end_body", 1):
                return ModuleTable.UserModule;
            case ("export", 1):
                DeclareExports(module, directive.Arguments[0], source);
                return module;
            case ("use_module", 1 or 2):
                imports.Add(ImportRequest.ForFile(source, module, directive));
                return module;
            case ("import", 1 or 2):
                imports.Add(ImportRequest.ForModule(source, module, directive));
                return module;
            case ("reexport", 1 or 2):
                imports.Add(ImportRequest.ForModule(source, module, directive, reexport: true));
                return module;
            case ("dynamic", 1):
                DeclarePredicates(module, directive.Arguments[0], dynamic: true, multifile: false, declarationKind: null, source);
                return module;
            case ("multifile", 1):
                DeclarePredicates(module, directive.Arguments[0], dynamic: false, multifile: true, declarationKind: null, source);
                return module;
            case ("discontiguous", 1):
                DeclarePredicates(
                    module,
                    directive.Arguments[0],
                    dynamic: false,
                    multifile: false,
                    declarationKind: "discontiguous",
                    source
                );
                return module;
            case ("meta_predicate", 1):
                DeclareMetaPredicates(module, directive.Arguments[0], source);
                return module;
            case ("op", 3):
            case ("set_prolog_flag", 2):
            case ("char_conversion", 2):
                _effectfulModules.Add(module);
                return module;
            default:
                _effectfulModules.Add(module);
                return module;
        }
    }

    private void DeclareModule(string module, SyntaxTerm exports, PrologLintSource source)
    {
        Modules.Catalog.Declare(module);
        DeclareExports(module, exports, source);
    }

    private void DeclareExports(string module, SyntaxTerm declarations, PrologLintSource source)
    {
        List<PredicateIndicator> exports = [];
        foreach (SyntaxTerm item in LintSyntax.SequenceItems(declarations))
        {
            if (!LintSyntax.TryIndicator(item, out PredicateIndicator indicator, out var nonterminal))
            {
                continue;
            }

            exports.Add(indicator);
            Declarations.Add(new DeclarationSite(module, indicator, nonterminal, "export", item.Span, source.FileName));
        }

        Modules.Declare(module, exports);
    }

    private void DeclarePredicates(
        string module,
        SyntaxTerm declarations,
        bool dynamic,
        bool multifile,
        string? declarationKind,
        PrologLintSource source
    )
    {
        foreach (SyntaxTerm item in LintSyntax.SequenceItems(declarations))
        {
            if (!LintSyntax.TryIndicator(item, out PredicateIndicator indicator, out var nonterminal))
            {
                continue;
            }

            if (dynamic || multifile)
            {
                Modules.Define(module, indicator);
                _ordinary.Add((module, indicator));
                _dynamicOrMultifile.Add((module, indicator));
            }

            if (dynamic)
            {
                Modules.DeclareDynamic(module, indicator);
            }

            if (multifile)
            {
                Modules.DeclareMultifile(module, indicator);
            }

            if (declarationKind is not null)
            {
                Declarations.Add(
                    new DeclarationSite(module, indicator, nonterminal, declarationKind, item.Span, source.FileName)
                );
            }
        }
    }

    private void DeclareMetaPredicates(string module, SyntaxTerm declarations, PrologLintSource source)
    {
        foreach (SyntaxTerm item in LintSyntax.SequenceItems(declarations))
        {
            if (!LintSyntax.TryPredicate(item, grammar: false, out PredicateIndicator indicator))
            {
                continue;
            }

            var arguments = new (int Position, int Extra)[indicator.Arity];
            char[] template = [.. Enumerable.Repeat('?', indicator.Arity)];
            if (item is CompoundTerm compound)
            {
                for (var index = 0; index < compound.Arity; index++)
                {
                    switch (compound.Arguments[index])
                    {
                        case IntegerTerm { Value: >= 0 } extra:
                            arguments[index] = (index, (int)extra.Value);
                            template[index] = ':';
                            break;
                        case AtomTerm { Name: ":" }:
                            arguments[index] = (index, 0);
                            template[index] = ':';
                            break;
                        default:
                            arguments[index] = (index, ModuleTable.OrdinaryArgument);
                            break;
                    }
                }
            }

            Modules.DeclareMeta(module, indicator.Name, indicator.Arity, arguments, new string(template));
            Declarations.Add(new DeclarationSite(module, indicator, false, "meta_predicate", item.Span, source.FileName));
        }
    }

    private void ResolveImport(ImportRequest request)
    {
        string? sourceModule = request.SourceModule;
        if (request.FileDirective)
        {
            sourceModule = ResolveModulePath(request);
            if (sourceModule is null)
            {
                MarkUnresolved(request, predicate: null);
                Imports.Add(request.ToSite(sourceModule));
                return;
            }
        }
        else if (sourceModule is null || !Modules.Catalog.Contains(sourceModule))
        {
            MarkUnresolved(request, predicate: null);
            Imports.Add(request.ToSite(sourceModule));
            return;
        }

        IEnumerable<PredicateIndicator> selected = request.Selective
            ? request.Bindings.Select(binding => binding.Indicator)
            : Modules.ExportsOf(sourceModule);
        HashSet<PredicateIndicator> selectedSet = [.. selected];
        HashSet<PredicateIndicator> exports = [.. Modules.ExportsOf(sourceModule)];
        foreach (PredicateIndicator predicate in selectedSet)
        {
            if (!exports.Contains(predicate))
            {
                MarkUnresolved(request, predicate);
                continue;
            }

            if (Modules.TryImport(request.Importer, predicate, sourceModule, out _))
            {
                if (request.Reexport)
                {
                    Modules.Declare(request.Importer, [predicate]);
                }
            }
            else
            {
                MarkUnresolved(request, predicate);
            }
        }

        Imports.Add(request.ToSite(sourceModule));
    }

    private string? ResolveModulePath(ImportRequest request)
    {
        if (request.Path is null || request.Path.StartsWith("library(", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var path = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(request.Source.FileName) ?? string.Empty, request.Path)
            );
            return _moduleByPath.TryGetValue(path, out string? module) ? module : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private void MarkUnresolved(ImportRequest request, PredicateIndicator? predicate)
    {
        if (predicate is { } selected)
        {
            _unresolvedImportPredicates.Add((request.Importer, selected));
        }
        else
        {
            _unresolvedImportModules.Add(request.Importer);
        }
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal sealed record ClauseContext(
        PrologLintSource Source,
        SyntaxTerm Clause,
        string Module,
        PredicateIndicator Indicator,
        bool Grammar
    );

    internal sealed record DeclarationSite(
        string Module,
        PredicateIndicator Indicator,
        bool Nonterminal,
        string Kind,
        SourceSpan Span,
        string FileName
    );

    internal readonly record struct LintResolution
    {
        private LintResolution(bool known, string? module, bool unknown = false)
        {
            Known = known;
            Module = module;
            IsUnknown = unknown;
        }

        internal static readonly LintResolution Undefined = new(false, null);
        internal static readonly LintResolution Unknown = new(true, null, unknown: true);

        internal bool Known { get; }
        internal string? Module { get; }
        internal bool IsUnknown { get; }
        internal bool IsDefined => Known && Module is not null;
        internal bool IsBuiltin => Known && Module is null && !IsUnknown;

        internal static LintResolution Defined(string? module) => new(true, module);
    }

    internal sealed record ImportSite(
        string Importer,
        string? SourceModule,
        bool Selective,
        bool Reexport,
        IReadOnlyList<ImportBinding> Bindings,
        SyntaxTerm SourceTerm,
        PrologLintSource Source
    );

    internal sealed record ImportBinding(PredicateIndicator Indicator, bool Nonterminal, SourceSpan Span);

    private sealed record ImportRequest(
        PrologLintSource Source,
        string Importer,
        string? SourceModule,
        string? Path,
        bool FileDirective,
        bool Selective,
        bool Reexport,
        CompoundTerm Directive,
        IReadOnlyList<ImportBinding> Bindings
    )
    {
        internal static ImportRequest ForFile(PrologLintSource source, string importer, CompoundTerm directive)
        {
            var path = directive.Arguments[0] is AtomTerm atom ? atom.Name : null;
            IReadOnlyList<ImportBinding> bindings = directive.Arity == 2 ? Indicators(directive.Arguments[1]) : [];
            return new ImportRequest(source, importer, null, path, true, directive.Arity == 2, false, directive, bindings);
        }

        internal static ImportRequest ForModule(
            PrologLintSource source,
            string importer,
            CompoundTerm directive,
            bool reexport = false
        )
        {
            var module = directive.Arguments[0] is AtomTerm atom ? atom.Name : null;
            IReadOnlyList<ImportBinding> bindings = directive.Arity == 2 ? Indicators(directive.Arguments[1]) : [];
            return new ImportRequest(source, importer, module, null, false, directive.Arity == 2, reexport, directive, bindings);
        }

        internal ImportSite ToSite(string? sourceModule) =>
            new(Importer, sourceModule, Selective, Reexport, Bindings, Directive.Arguments[0], Source);

        private static List<ImportBinding> Indicators(SyntaxTerm term)
        {
            List<ImportBinding> result = [];
            foreach (SyntaxTerm item in LintSyntax.SequenceItems(term))
            {
                if (LintSyntax.TryIndicator(item, out PredicateIndicator indicator, out var nonterminal))
                {
                    result.Add(new ImportBinding(indicator, nonterminal, item.Span));
                }
            }

            return result;
        }
    }
}
