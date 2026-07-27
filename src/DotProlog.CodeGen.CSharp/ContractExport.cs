namespace DotProlog.CodeGen.CSharp;

/// <summary>One predicate a module exposes to .NET.</summary>
/// <param name="PredicateName">The Prolog predicate's name.</param>
/// <param name="Arity">The Prolog predicate's arity.</param>
/// <param name="Determinism">How many solutions it has.</param>
/// <param name="Arguments">Its arguments, in Prolog order.</param>
/// <param name="ClrName">
/// The method name to generate, when the predicate's own name is a poor one in C#. This is the
/// equivalent of F#'s <c>[CompiledName]</c>, and it is also how a predicate exported in more than one
/// mode gives each mode a distinct method.
/// </param>
public sealed record ContractExport(
    string PredicateName,
    int Arity,
    Determinism Determinism,
    IReadOnlyList<ContractArgument> Arguments,
    string? ClrName = null
)
{
    /// <summary>The arguments the caller supplies.</summary>
    public IEnumerable<ContractArgument> Inputs => Arguments.Where(argument => argument.Mode == ArgumentMode.In);

    /// <summary>The arguments the predicate produces.</summary>
    public IEnumerable<ContractArgument> Outputs => Arguments.Where(argument => argument.Mode == ArgumentMode.Out);

    /// <summary>Whether the predicate can yield more than one solution.</summary>
    public bool IsStreaming => Determinism is Determinism.Multi or Determinism.Nondet;
}
