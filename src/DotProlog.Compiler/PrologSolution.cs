using DotProlog.Runtime;

namespace DotProlog.Compiler;

/// <summary>
/// One answer to a query: the value each of its variables was bound to.
/// </summary>
/// <remarks>
/// Bindings are marshalled when the solution is produced, so a solution stays valid after the query
/// has moved on to the next answer or been abandoned entirely.
/// </remarks>
public sealed class PrologSolution
{
    private readonly Dictionary<string, PrologValue> _bindings;

    internal PrologSolution(Dictionary<string, PrologValue> bindings) => _bindings = bindings;

    /// <summary>The variables of the query and what they were bound to.</summary>
    public IReadOnlyDictionary<string, PrologValue> Bindings => _bindings;

    /// <summary>The value bound to <paramref name="variableName"/>.</summary>
    /// <exception cref="KeyNotFoundException">The query has no such variable.</exception>
    public PrologValue this[string variableName] => _bindings[variableName];

    /// <summary>Looks up a variable without throwing when the query does not have it.</summary>
    public bool TryGetValue(string variableName, out PrologValue value) => _bindings.TryGetValue(variableName, out value!);

    /// <inheritdoc />
    public override string ToString() =>
        _bindings.Count == 0 ? "true" : string.Join(", ", _bindings.Select(pair => $"{pair.Key} = {pair.Value}"));
}
