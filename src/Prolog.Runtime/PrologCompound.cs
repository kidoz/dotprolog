namespace Prolog.Runtime;

/// <summary>A marshalled compound term. Lists appear as nested <c>'.'/2</c> terms.</summary>
/// <param name="Name">The functor name.</param>
/// <param name="Arguments">The arguments.</param>
public sealed record PrologCompound(string Name, IReadOnlyList<PrologValue> Arguments) : PrologValue
{
    /// <inheritdoc />
    public override string ToString() => $"{Name}({string.Join(",", Arguments)})";
}
