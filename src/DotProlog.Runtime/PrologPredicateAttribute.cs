namespace DotProlog.Runtime;

/// <summary>Declares a static C# method as a deterministic Prolog foreign predicate.</summary>
/// <remarks>
/// The method must return <see cref="bool"/> and accept one <see langword="ref"/>
/// <see cref="PrologContext"/> followed by <see cref="Arity"/> <see cref="Cell"/> arguments.
/// Registration code is generated at build time; no runtime discovery occurs.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PrologPredicateAttribute : Attribute
{
    /// <summary>The Prolog predicate name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The predicate arity, from zero through 255.</summary>
    public int Arity { get; set; }

    /// <summary>An optional module whose name qualifies the predicate.</summary>
    public string? Module { get; set; }

    /// <summary>The declared determinism. Only <c>det</c> is currently supported.</summary>
    public string Determinism { get; set; } = "det";
}
