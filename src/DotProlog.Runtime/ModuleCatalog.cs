namespace DotProlog.Runtime;

/// <summary>A predicate name and arity as recorded by the ISO module system.</summary>
public readonly record struct ModulePredicateIndicator(string Name, int Arity)
{
    /// <inheritdoc />
    public override string ToString() => $"{Name}/{Arity.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>Program-owned metadata for ISO/IEC 13211-2 modules.</summary>
/// <remarks>
/// Calls still compile to ordinary qualified functors. This catalog is consulted only by module
/// visibility, reflection, and context-sensitive built-ins, so ordinary dispatch stays unchanged.
/// </remarks>
public sealed class ModuleCatalog
{
    private readonly Dictionary<string, ModuleDefinition> _modules = new(StringComparer.Ordinal);

    /// <summary>Creates a catalog containing the required <c>user</c> module.</summary>
    public ModuleCatalog() => Declare("user");

    /// <summary>All modules in declaration order.</summary>
    public IReadOnlyCollection<ModuleDefinition> Definitions => _modules.Values;

    /// <summary>Whether a module exists.</summary>
    public bool Contains(string module) => _modules.ContainsKey(module);

    /// <summary>Returns a module definition, creating it when an interface is prepared.</summary>
    public ModuleDefinition Declare(string module)
    {
        ArgumentException.ThrowIfNullOrEmpty(module);
        if (!_modules.TryGetValue(module, out ModuleDefinition? definition))
        {
            definition = new ModuleDefinition(module);
            _modules.Add(module, definition);
        }

        return definition;
    }

    /// <summary>Returns a module definition when it exists.</summary>
    public bool TryGet(string module, out ModuleDefinition? definition) => _modules.TryGetValue(module, out definition);
}

/// <summary>The interface, visibility, and predicate properties of one module.</summary>
public sealed class ModuleDefinition
{
    private readonly Dictionary<ModulePredicateIndicator, ModulePredicateDefinition> _predicates = [];
    private readonly Dictionary<ModulePredicateIndicator, string> _imports = [];
    private readonly Queue<ModuleReaderState> _preparedBodies = [];

    internal ModuleDefinition(string name)
    {
        Name = name;
        Operators = new OperatorTable();
    }

    /// <summary>The module atom.</summary>
    public string Name { get; }

    /// <summary>Whether an ISO interface for the module has been closed.</summary>
    public bool InterfacePrepared { get; set; }

    /// <summary>The operator table accumulated by the interface and bodies.</summary>
    public OperatorTable Operators { get; internal set; }

    /// <summary>The character conversions accumulated by the interface and bodies.</summary>
    public CharacterConversionTable CharacterConversions { get; internal set; } = new();

    /// <summary>The flag values accumulated by the interface and bodies.</summary>
    public PrologFlags Flags { get; internal set; } = new();

    /// <summary>Seeds module reader state from the surrounding Prolog text.</summary>
    public void SeedReaderState(OperatorTable operators, CharacterConversionTable conversions, PrologFlags flags)
    {
        ArgumentNullException.ThrowIfNull(operators);
        ArgumentNullException.ThrowIfNull(conversions);
        ArgumentNullException.ThrowIfNull(flags);
        Operators = operators.Copy();
        CharacterConversions = conversions.Copy();
        Flags = flags.Copy();
    }

    /// <summary>Records the reader state in force at the start of a body.</summary>
    public void RecordPreparedBody() =>
        _preparedBodies.Enqueue(new ModuleReaderState(Operators.Copy(), CharacterConversions.Copy(), Flags.Copy()));

    /// <summary>Returns the next body-start reader state recorded while parsing.</summary>
    public bool TryTakePreparedBody(out ModuleReaderState? state) => _preparedBodies.TryDequeue(out state);

    /// <summary>Predicates defined or declared by this module.</summary>
    public IReadOnlyCollection<ModulePredicateDefinition> Predicates => _predicates.Values;

    /// <summary>Visible imports keyed by their unqualified predicate indicator.</summary>
    public IReadOnlyDictionary<ModulePredicateIndicator, string> Imports => _imports;

    /// <summary>Returns predicate metadata, creating it when a declaration first names the predicate.</summary>
    public ModulePredicateDefinition Predicate(ModulePredicateIndicator indicator)
    {
        if (!_predicates.TryGetValue(indicator, out ModulePredicateDefinition? predicate))
        {
            predicate = new ModulePredicateDefinition(indicator, Name);
            _predicates.Add(indicator, predicate);
        }

        return predicate;
    }

    /// <summary>Returns predicate metadata when present.</summary>
    public bool TryPredicate(ModulePredicateIndicator indicator, out ModulePredicateDefinition? predicate) =>
        _predicates.TryGetValue(indicator, out predicate);

    /// <summary>Adds an unqualified import unless another module already supplies that name.</summary>
    public bool TryImport(ModulePredicateIndicator indicator, string from, out string? conflictingModule)
    {
        if (_imports.TryGetValue(indicator, out var existing))
        {
            conflictingModule = existing;
            return existing == from;
        }

        _imports.Add(indicator, from);
        conflictingModule = null;
        return true;
    }
}

/// <summary>An independent snapshot of a module body's initial reader and flag state.</summary>
public sealed record ModuleReaderState(
    OperatorTable Operators,
    CharacterConversionTable CharacterConversions,
    PrologFlags Flags
);

/// <summary>ISO properties known for one procedure defined by a module.</summary>
public sealed class ModulePredicateDefinition
{
    private readonly List<ModuleClauseDefinition> _clauses = [];

    internal ModulePredicateDefinition(ModulePredicateIndicator indicator, string definingModule)
    {
        Indicator = indicator;
        DefiningModule = definingModule;
    }

    /// <summary>The unqualified predicate indicator.</summary>
    public ModulePredicateIndicator Indicator { get; }

    /// <summary>The module containing the clauses.</summary>
    public string DefiningModule { get; }

    /// <summary>Whether the interface exports the procedure.</summary>
    public bool Exported { get; set; }

    /// <summary>Whether the module contains or declares the procedure.</summary>
    public bool Defined { get; set; }

    /// <summary>Whether the procedure uses the dynamic database.</summary>
    public bool Dynamic { get; set; }

    /// <summary>Whether clauses may occur in more than one Prolog text.</summary>
    public bool Multifile { get; set; }

    /// <summary>The ISO metapredicate mode indicators, or null for an ordinary predicate.</summary>
    public string? MetapredicateTemplate { get; set; }

    /// <summary>
    /// Adds a detached static clause term used by the module-aware <c>clause/2</c> predicate.
    /// </summary>
    /// <remarks>This metadata does not make the executable procedure dynamic.</remarks>
    public void AddStaticClause(ReadOnlySpan<Cell> cells, int root) =>
        _clauses.Add(new ModuleClauseDefinition(TermBuffer.FromCells(cells), root));

    /// <summary>The retained static clauses, in preparation order.</summary>
    internal IReadOnlyList<ModuleClauseDefinition> StaticClauses => _clauses;
}

/// <summary>A detached term for a static module clause.</summary>
internal sealed record ModuleClauseDefinition(TermBuffer Term, int Root);
