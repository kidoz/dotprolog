using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>
/// What each loaded module exports, what each imports, and which of a predicate's arguments are
/// goals. This outlives a single load, because a module compiled now may be imported later.
/// </summary>
/// <remarks>
/// A predicate <c>p/1</c> defined in module <c>m</c> is compiled under the name <c>m:p</c>, which is
/// an ordinary functor as far as everything downstream is concerned. Nothing in the engine knows
/// that modules exist; resolution is a rewrite performed while loading.
/// </remarks>
public sealed class ModuleTable
{
    /// <summary>The module a file with no <c>:- module/2</c> declaration belongs to.</summary>
    public const string UserModule = "user";

    /// <summary>An argument that is not a goal.</summary>
    public const int OrdinaryArgument = -1;

    /// <summary>An argument that is a clause: a head, or a head and a body.</summary>
    public const int ClauseArgument = -2;

    /// <summary>An argument that is a clause head on its own.</summary>
    public const int HeadArgument = -3;

    private readonly Dictionary<string, HashSet<PredicateIndicator>> _exports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<PredicateIndicator, string>> _imports = new(StringComparer.Ordinal);
    private readonly Dictionary<PredicateIndicator, int[]> _metaArguments = [];
    private readonly Dictionary<(string Module, PredicateIndicator Predicate), int[]> _moduleMetaArguments = [];
    private readonly Dictionary<string, string> _loaded = new(StringComparer.Ordinal);
    private readonly HashSet<MultifileKey> _multifile = [];
    private readonly Dictionary<MultifileKey, List<(SyntaxTerm Head, SyntaxTerm? Body)>> _multifileClauses = [];

    /// <summary>Creates a table holding the meta-predicates every program starts with.</summary>
    public ModuleTable(ModuleCatalog? catalog = null)
    {
        Catalog = catalog ?? new ModuleCatalog();
        // Which arguments of a predicate are goals, and how many arguments each will gain before it
        // is called. Zero means the argument is called as it stands; a positive number means it is a
        // closure that call/N will extend, so it has to carry its module with it rather than being
        // resolved here.
        DeclareMeta("findall", 3, [(1, 0)]);
        DeclareMeta("findall", 4, [(1, 0)]);
        DeclareMeta("bagof", 3, [(1, 0)]);
        DeclareMeta("setof", 3, [(1, 0)]);
        DeclareMeta("aggregate_all", 3, [(1, 0)]);
        DeclareMeta("forall", 2, [(0, 0), (1, 0)]);
        DeclareMeta("once", 1, [(0, 0)]);
        DeclareMeta("ignore", 1, [(0, 0)]);
        DeclareMeta("not", 1, [(0, 0)]);
        DeclareMeta("catch", 3, [(0, 0), (2, 0)]);
        DeclareMeta("with_output_to", 2, [(1, 0)]);
        DeclareMeta("call", 1, [(0, 0)]);

        // The database predicates take clauses rather than goals, and a clause names a predicate
        // just as much as a call does: assertz(fact(x)) inside a module is that module's fact/1.
        DeclareMeta("assert", 1, [(0, ClauseArgument)]);
        DeclareMeta("asserta", 1, [(0, ClauseArgument)]);
        DeclareMeta("assertz", 1, [(0, ClauseArgument)]);
        DeclareMeta("retract", 1, [(0, ClauseArgument)]);
        DeclareMeta("retractall", 1, [(0, HeadArgument)]);
        DeclareMeta("clause", 2, [(0, HeadArgument)]);

        for (var extra = 1; extra <= 7; extra++)
        {
            DeclareMeta("call", extra + 1, [(0, extra)]);
        }

        for (var lists = 1; lists <= 4; lists++)
        {
            DeclareMeta("maplist", lists + 1, [(0, lists)]);
        }

        DeclareMeta("foldl", 4, [(0, 3)]);
        DeclareMeta("foldl", 5, [(0, 4)]);
        DeclareMeta("include", 3, [(0, 1)]);
        DeclareMeta("exclude", 3, [(0, 1)]);
        DeclareMeta("partition", 4, [(0, 1)]);
        DeclareMeta("predsort", 3, [(0, 3)]);
        DeclareMeta("phrase", 2, [(0, 2)]);
        DeclareMeta("phrase", 3, [(0, 2)]);
        DeclareMeta("predicate_property", 2, [(0, HeadArgument)]);
    }

    /// <summary>The runtime-visible catalog populated alongside compiler resolution tables.</summary>
    public ModuleCatalog Catalog { get; }

    /// <summary>Records that <paramref name="module"/> exports <paramref name="exports"/>.</summary>
    public void Declare(string module, IEnumerable<PredicateIndicator> exports)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(exports);

        if (!_exports.TryGetValue(module, out HashSet<PredicateIndicator>? set))
        {
            set = [];
            _exports[module] = set;
        }

        ModuleDefinition definition = Catalog.Declare(module);
        foreach (PredicateIndicator export in exports)
        {
            set.Add(export);
            definition.Predicate(ToRuntime(export)).Exported = true;
        }
    }

    /// <summary>Whether <paramref name="module"/> exports <paramref name="predicate"/>.</summary>
    public bool Exports(string module, PredicateIndicator predicate) =>
        (_exports.TryGetValue(module, out HashSet<PredicateIndicator>? set) && set.Contains(predicate))
        || (
            Catalog.TryGet(module, out ModuleDefinition? definition)
            && definition!.TryPredicate(ToRuntime(predicate), out ModulePredicateDefinition? metadata)
            && metadata!.Exported
        );

    /// <summary>What <paramref name="module"/> exports, or nothing if it declared no module.</summary>
    public IReadOnlyCollection<PredicateIndicator> ExportsOf(string module)
    {
        HashSet<PredicateIndicator> exports = _exports.TryGetValue(module, out HashSet<PredicateIndicator>? declared)
            ? [.. declared]
            : [];
        if (Catalog.TryGet(module, out ModuleDefinition? definition))
        {
            exports.UnionWith(
                definition!.Predicates.Where(predicate => predicate.Exported).Select(predicate => ToCompiler(predicate.Indicator))
            );
        }

        return exports;
    }

    /// <summary>
    /// Records that <paramref name="importer"/> takes <paramref name="predicate"/> from
    /// <paramref name="from"/>, unless another module already supplies the same visible name.
    /// </summary>
    public bool TryImport(string importer, PredicateIndicator predicate, string from, out string? conflictingModule)
    {
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(from);

        if (!_imports.TryGetValue(importer, out Dictionary<PredicateIndicator, string>? map))
        {
            map = [];
            _imports[importer] = map;
        }

        if (map.TryGetValue(predicate, out var existing))
        {
            conflictingModule = existing;
            return existing == from;
        }

        if (Defines(importer, predicate))
        {
            conflictingModule = importer;
            return false;
        }

        map[predicate] = from;
        _ = Catalog.Declare(importer).TryImport(ToRuntime(predicate), from, out _);
        conflictingModule = null;
        return true;
    }

    /// <summary>The module <paramref name="importer"/> imported <paramref name="predicate"/> from, if any.</summary>
    public string? ImportedFrom(string importer, PredicateIndicator predicate) =>
        _imports.TryGetValue(importer, out Dictionary<PredicateIndicator, string>? map)
        && map.TryGetValue(predicate, out var from)
            ? from
            : Catalog.TryGet(importer, out ModuleDefinition? definition)
                && definition!.Imports.TryGetValue(ToRuntime(predicate), out string? catalogFrom)
                    ? catalogFrom
                    : null;

    /// <summary>Follows reexports to the module that contains the procedure's clauses.</summary>
    public string? DefiningModuleOf(string importer, PredicateIndicator predicate)
    {
        string? current = ImportedFrom(importer, predicate);
        HashSet<string> visited = [];
        while (current is not null && visited.Add(current))
        {
            if (Defines(current, predicate))
            {
                return current;
            }

            string? next = ImportedFrom(current, predicate);
            if (next is null)
            {
                return current;
            }

            current = next;
        }

        return current;
    }

    /// <summary>Declares which arguments of a predicate are goals.</summary>
    /// <param name="name">Predicate name.</param>
    /// <param name="arity">Predicate arity.</param>
    /// <param name="arguments">Zero-based argument positions, each with the arguments it will gain.</param>
    public void DeclareMeta(string name, int arity, IReadOnlyList<(int Position, int Extra)> arguments)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(arguments);

        var positions = new int[arity];
        Array.Fill(positions, OrdinaryArgument);

        foreach ((var position, var extra) in arguments)
        {
            if (position >= 0 && position < arity)
            {
                positions[position] = extra;
            }
        }

        _metaArguments[new PredicateIndicator(name, arity)] = positions;
    }

    /// <summary>Declares an ISO module-local metapredicate template.</summary>
    public void DeclareMeta(
        string module,
        string name,
        int arity,
        IReadOnlyList<(int Position, int Extra)> arguments,
        string template
    )
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(template);

        var indicator = new PredicateIndicator(name, arity);
        var positions = new int[arity];
        Array.Fill(positions, OrdinaryArgument);
        foreach ((var position, var extra) in arguments)
        {
            if (position >= 0 && position < arity)
            {
                positions[position] = extra;
            }
        }

        _moduleMetaArguments[(module, indicator)] = positions;
        Catalog.Declare(module).Predicate(ToRuntime(indicator)).MetapredicateTemplate = template;
    }

    /// <summary>
    /// For each argument of <paramref name="predicate"/>, how many arguments it will gain before
    /// being called, or -1 when it is not a goal. Null when the predicate is not a meta-predicate.
    /// </summary>
    public int[]? MetaArgumentsOf(PredicateIndicator predicate) =>
        _metaArguments.TryGetValue(predicate, out var positions) ? positions : null;

    /// <summary>Returns the module-local, imported, or predefined metapredicate template.</summary>
    public int[]? MetaArgumentsOf(string module, PredicateIndicator predicate)
    {
        if (_moduleMetaArguments.TryGetValue((module, predicate), out var local))
        {
            return local;
        }

        if (
            Catalog.TryGet(module, out ModuleDefinition? localDefinition)
            && localDefinition!.TryPredicate(ToRuntime(predicate), out ModulePredicateDefinition? localMetadata)
            && localMetadata!.MetapredicateTemplate is string localTemplate
        )
        {
            return RuntimeMetaArguments(localTemplate);
        }

        string? from = DefiningModuleOf(module, predicate);
        if (from is not null && _moduleMetaArguments.TryGetValue((from, predicate), out var imported))
        {
            return imported;
        }

        if (
            from is not null
            && Catalog.TryGet(from, out ModuleDefinition? importedDefinition)
            && importedDefinition!.TryPredicate(ToRuntime(predicate), out ModulePredicateDefinition? importedMetadata)
            && importedMetadata!.MetapredicateTemplate is string importedTemplate
        )
        {
            return RuntimeMetaArguments(importedTemplate);
        }

        return MetaArgumentsOf(predicate);
    }

    private static int[] RuntimeMetaArguments(string template) =>
        [.. template.Select(mode => mode == ':' ? 0 : OrdinaryArgument)];

    private static PredicateIndicator ToCompiler(ModulePredicateIndicator indicator) =>
        new(indicator.Name, indicator.Arity);

    /// <summary>Records a procedure defined by a module.</summary>
    public void Define(string module, PredicateIndicator predicate) =>
        Catalog.Declare(module).Predicate(ToRuntime(predicate)).Defined = true;

    /// <summary>Whether a module contains or declares a local procedure.</summary>
    public bool Defines(string module, PredicateIndicator predicate) =>
        Catalog.TryGet(module, out ModuleDefinition? definition)
        && definition!.TryPredicate(ToRuntime(predicate), out ModulePredicateDefinition? metadata)
        && metadata!.Defined;

    /// <summary>Marks a module procedure dynamic.</summary>
    public void DeclareDynamic(string module, PredicateIndicator predicate) =>
        Catalog.Declare(module).Predicate(ToRuntime(predicate)).Dynamic = true;

    /// <summary>
    /// Records that <paramref name="path"/> is being loaded, before it is, so that two files that
    /// use each other do not load forever.
    /// </summary>
    public void BeginLoad(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _loaded[path] = UserModule;
    }

    /// <summary>Records which module a loaded file declared.</summary>
    public void RecordLoad(string path, string module)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(module);
        _loaded[path] = module;
    }

    /// <summary>The module a file declared, or null when the file has not been loaded.</summary>
    public string? LoadedModuleOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _loaded.TryGetValue(path, out var module) ? module : null;
    }

    /// <summary>Declares a predicate to accept static clauses from more than one source unit.</summary>
    public void DeclareMultifile(string module, PredicateIndicator predicate)
    {
        _multifile.Add(new MultifileKey(module, predicate));
        Catalog.Declare(module).Predicate(ToRuntime(predicate)).Multifile = true;
    }

    /// <summary>Whether a predicate is a persistent static multifile predicate.</summary>
    public bool IsMultifile(string module, PredicateIndicator predicate) =>
        _multifile.Contains(new MultifileKey(module, predicate));

    /// <summary>
    /// Appends clauses contributed by one source unit and returns every clause accumulated for the
    /// multifile predicate in load order.
    /// </summary>
    public IReadOnlyList<(SyntaxTerm Head, SyntaxTerm? Body)> AppendMultifileClauses(
        string module,
        PredicateIndicator predicate,
        IEnumerable<(SyntaxTerm Head, SyntaxTerm? Body)> clauses
    )
    {
        var key = new MultifileKey(module, predicate);
        if (!_multifileClauses.TryGetValue(key, out List<(SyntaxTerm Head, SyntaxTerm? Body)>? accumulated))
        {
            accumulated = [];
            _multifileClauses[key] = accumulated;
        }

        accumulated.AddRange(clauses);
        return accumulated;
    }

    /// <summary>The compiled name of <paramref name="predicate"/> inside <paramref name="module"/>.</summary>
    public static string QualifiedName(string module, string predicate) =>
        module == UserModule ? predicate : $"{module}:{predicate}";

    private static ModulePredicateIndicator ToRuntime(PredicateIndicator predicate) =>
        new(predicate.Name, predicate.Arity);

    private readonly record struct MultifileKey(string Module, PredicateIndicator Predicate);
}

/// <summary>A predicate named by its name and arity.</summary>
/// <param name="Name">Predicate name.</param>
/// <param name="Arity">Number of arguments.</param>
public readonly record struct PredicateIndicator(string Name, int Arity)
{
    /// <inheritdoc />
    public override string ToString() => $"{Name}/{Arity.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
