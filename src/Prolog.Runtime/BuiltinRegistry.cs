namespace Prolog.Runtime;

/// <summary>
/// The native predicates a program may call. Entries are registered explicitly — there is no
/// reflection-based discovery — which keeps the registry trim-safe and usable under NativeAOT.
/// </summary>
public sealed class BuiltinRegistry
{
    private readonly SymbolTable _symbols;
    private readonly List<PrologBuiltin> _implementations = [];
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
        _names.Add($"{name}/{arity}");
        _idByFunctor[functorId] = id;
        return id;
    }

    /// <summary>Looks up the builtin registered for a functor identifier.</summary>
    public bool TryGetId(int functorId, out int builtinId) => _idByFunctor.TryGetValue(functorId, out builtinId);

    /// <summary>Returns the display name of builtin <paramref name="builtinId"/>, such as <c>write/1</c>.</summary>
    public string NameOf(int builtinId) => _names[builtinId];

    internal PrologBuiltin Implementation(int builtinId) => _implementations[builtinId];
}
