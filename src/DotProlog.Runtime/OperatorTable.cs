namespace DotProlog.Runtime;

/// <summary>
/// The operator definitions in force while reading. A table starts from the ISO default set and can
/// be extended or overridden through <see cref="Define"/>, which is what <c>op/3</c> maps onto.
/// </summary>
public sealed class OperatorTable
{
    private readonly Dictionary<string, PrologOperator> _prefix = [];
    private readonly Dictionary<string, PrologOperator> _infixOrPostfix = [];
    private readonly List<PrologOperator[]> _versions =
    [
        [],
    ];

    /// <summary>Creates a table containing the ISO default operators.</summary>
    public OperatorTable()
        : this(includeExtensions: true) { }

    /// <summary>Creates the ISO table and optionally adds DotProlog's predefined extensions.</summary>
    internal OperatorTable(bool includeExtensions)
    {
        DefineDefaults(includeExtensions);
    }

    /// <summary>The immutable operator-table version current when this property is read.</summary>
    internal int Version => _versions.Count - 1;

    /// <summary>
    /// Installs an operator. A priority of zero removes the definition in the matching class
    /// (prefix, or infix/postfix), matching <c>op/3</c>.
    /// </summary>
    public void Define(int priority, OperatorType type, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegative(priority);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(priority, 1200);

        Dictionary<string, PrologOperator> target = type is OperatorType.Fx or OperatorType.Fy ? _prefix : _infixOrPostfix;
        if (priority == 0)
        {
            if (!target.Remove(name))
            {
                return;
            }

            AddVersion();
            return;
        }

        var definition = new PrologOperator(priority, type, name);
        if (target.TryGetValue(name, out PrologOperator previous) && previous == definition)
        {
            return;
        }

        target[name] = definition;
        AddVersion();
    }

    /// <summary>Looks up a prefix definition for <paramref name="name"/>.</summary>
    public bool TryGetPrefix(string name, out PrologOperator op) => _prefix.TryGetValue(name, out op);

    /// <summary>Looks up an infix or postfix definition for <paramref name="name"/>.</summary>
    public bool TryGetInfixOrPostfix(string name, out PrologOperator op) => _infixOrPostfix.TryGetValue(name, out op);

    /// <summary>Whether <paramref name="name"/> has any operator definition.</summary>
    public bool IsOperator(string name) => _prefix.ContainsKey(name) || _infixOrPostfix.ContainsKey(name);

    /// <summary>
    /// Every definition, ordered by name and then by specifier. The order is fixed rather than
    /// insertion-dependent so that <c>current_op/3</c> enumerates the same way every run.
    /// </summary>
    public PrologOperator[] All() => _versions[^1];

    /// <summary>Returns the immutable definitions held by a prior table version.</summary>
    internal ReadOnlySpan<PrologOperator> Entries(int version) => _versions[version];

    /// <summary>The highest priority of any definition for <paramref name="name"/>, or zero if it is not an operator.</summary>
    public int MaxPriority(string name)
    {
        var priority = 0;
        if (_prefix.TryGetValue(name, out PrologOperator prefix))
        {
            priority = prefix.Priority;
        }

        if (_infixOrPostfix.TryGetValue(name, out PrologOperator other) && other.Priority > priority)
        {
            priority = other.Priority;
        }

        return priority;
    }

    /// <summary>
    /// Reports the ISO permission conflict that would prevent a definition. This is shared with the
    /// reader so an invalid <c>op/3</c> directive never changes parsing before the runtime goal
    /// raises its error.
    /// </summary>
    internal OperatorDefinitionConflict DefinitionConflict(int priority, OperatorType type, string name)
    {
        if (name == ",")
        {
            return OperatorDefinitionConflict.Modify;
        }

        if (priority == 0)
        {
            return OperatorDefinitionConflict.None;
        }

        var requestedInfix = type is OperatorType.Xfx or OperatorType.Xfy or OperatorType.Yfx;
        if (name is "[]" or "{}" || (name == "|" && (priority <= 1000 || !requestedInfix)))
        {
            return OperatorDefinitionConflict.Create;
        }

        var requestedPostfix = type is OperatorType.Xf or OperatorType.Yf;
        if (
            (requestedInfix || requestedPostfix)
            && _infixOrPostfix.TryGetValue(name, out PrologOperator existing)
            && existing.IsInfix != requestedInfix
        )
        {
            return OperatorDefinitionConflict.Create;
        }

        return OperatorDefinitionConflict.None;
    }

    private void DefineDefaults(bool includeExtensions)
    {
        Define(1200, OperatorType.Xfx, ":-");
        Define(1200, OperatorType.Xfx, "-->");
        Define(1200, OperatorType.Fx, ":-");
        Define(1200, OperatorType.Fx, "?-");
        foreach (var name in (string[])["meta_predicate", "module", "use_module"])
        {
            Define(1150, OperatorType.Fx, name);
        }

        Define(1100, OperatorType.Xfy, ";");
        Define(1105, OperatorType.Xfy, "|");
        Define(1050, OperatorType.Xfy, "->");
        // Part 3 requires an additional grammar control to remain ordinary nonterminal syntax in a
        // strict processor. ClauseCompiler and DcgTranslator decide whether this is executable
        // soft cut or an ordinary nonterminal after the reader has built the term.
        Define(1050, OperatorType.Xfy, "*->");
        Define(1000, OperatorType.Xfy, ",");
        Define(900, OperatorType.Fy, "\\+");

        foreach (
            var name in (string[])
                ["=", "\\=", "==", "\\==", "@<", "@>", "@=<", "@>=", "=..", "is", "=:=", "=\\=", "<", ">", "=<", ">="]
        )
        {
            Define(700, OperatorType.Xfx, name);
        }

        Define(600, OperatorType.Xfy, ":");
        foreach (var name in (string[])["+", "-", "/\\", "\\/", "xor"])
        {
            Define(500, OperatorType.Yfx, name);
        }

        foreach (var name in (string[])["*", "/", "//", "rem", "mod", "div", "<<", ">>"])
        {
            Define(400, OperatorType.Yfx, name);
        }

        Define(200, OperatorType.Xfx, "**");
        Define(200, OperatorType.Xfy, "^");
        Define(200, OperatorType.Fy, "-");
        Define(200, OperatorType.Fy, "+");
        Define(200, OperatorType.Fy, "\\");

        if (!includeExtensions)
        {
            return;
        }

        foreach (var name in (string[])["dynamic", "discontiguous", "ensure_loaded", "include", "initialization", "multifile"])
        {
            Define(1150, OperatorType.Fx, name);
        }

        Define(990, OperatorType.Xfx, ":=");
        Define(100, OperatorType.Yfx, ".");
        Define(1, OperatorType.Fx, "$");
    }

    private void AddVersion() =>
        _versions.Add([
            .. _prefix
                .Values.Concat(_infixOrPostfix.Values)
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .ThenBy(entry => entry.Type),
        ]);
}

internal enum OperatorDefinitionConflict
{
    None,
    Create,
    Modify,
}
