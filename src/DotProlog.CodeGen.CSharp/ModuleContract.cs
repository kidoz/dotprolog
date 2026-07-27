namespace DotProlog.CodeGen.CSharp;

/// <summary>A module's .NET surface, read from its <c>.dpli</c> contract file.</summary>
/// <param name="ClrTypeName">The type name to generate, without the leading <c>I</c> or namespace.</param>
/// <param name="Namespace">The namespace to generate into.</param>
/// <param name="Exports">The predicates the module exposes.</param>
public sealed record ModuleContract(string ClrTypeName, string Namespace, IReadOnlyList<ContractExport> Exports);
