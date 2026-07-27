namespace DotProlog.Runtime;

/// <summary>
/// The native predicates a program may call. Entries are registered explicitly — there is no
/// reflection-based discovery — which keeps the registry trim-safe and usable under NativeAOT.
/// </summary>
public sealed class BuiltinRegistry
{
    private readonly SymbolTable _symbols;
    private readonly List<PrologBuiltin> _implementations = [];
    private readonly List<PrologRetry?> _retries = [];
    private readonly List<string> _names = [];
    private readonly Dictionary<int, int> _idByFunctor = [];

    /// <summary>Creates a registry over <paramref name="symbols"/>.</summary>
    public BuiltinRegistry(SymbolTable symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        _symbols = symbols;
    }

    /// <summary>Registers <paramref name="implementation"/> as <c>name/arity</c> and returns its identifier.</summary>
    public int Register(string name, int arity, PrologBuiltin implementation)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(implementation);

        int functorId = _symbols.InternFunctor(name, arity);
        int id = _implementations.Count;
        _implementations.Add(implementation);
        _retries.Add(null);
        _names.Add($"{name}/{arity}");
        _idByFunctor[functorId] = id;
        return id;
    }

    /// <summary>
    /// Registers a native predicate that can yield more than one solution. The first call runs
    /// <paramref name="implementation"/>; each redo runs <paramref name="retry"/>. Either may call
    /// <see cref="Machine.PushRetry(long)"/> to offer a further solution.
    /// </summary>
    public int RegisterNondeterministic(string name, int arity, PrologBuiltin implementation, PrologRetry retry)
    {
        ArgumentNullException.ThrowIfNull(retry);

        int id = Register(name, arity, implementation);
        _retries[id] = retry;
        return id;
    }

    /// <summary>Looks up the builtin registered for a functor identifier.</summary>
    public bool TryGetId(int functorId, out int builtinId) => _idByFunctor.TryGetValue(functorId, out builtinId);

    /// <summary>Returns the display name of builtin <paramref name="builtinId"/>, such as <c>write/1</c>.</summary>
    public string NameOf(int builtinId) => _names[builtinId];

    internal PrologBuiltin Implementation(int builtinId) => _implementations[builtinId];

    internal PrologRetry Retry(int builtinId) =>
        _retries[builtinId] ?? throw new PrologException($"{_names[builtinId]} is not a nondeterministic builtin.");
}
