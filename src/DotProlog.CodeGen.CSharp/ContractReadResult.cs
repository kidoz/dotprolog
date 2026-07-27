using DotProlog.Syntax;

namespace DotProlog.CodeGen.CSharp;

/// <summary>The outcome of reading a <c>.dpli</c> contract.</summary>
/// <param name="Contract">The contract, or <see langword="null"/> when it could not be read.</param>
/// <param name="Diagnostics">Every diagnostic raised while reading.</param>
public sealed record ContractReadResult(ModuleContract? Contract, IReadOnlyList<Diagnostic> Diagnostics)
{
    /// <summary>Whether the contract was read without errors.</summary>
    public bool Success => Contract is not null;
}
