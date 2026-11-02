namespace DotProlog.Compiler;

/// <summary>Non-executing editor help for a predefined predicate.</summary>
/// <param name="Signature">Predicate name and callable arity.</param>
/// <param name="Description">Short description of the predefined behavior.</param>
/// <param name="Availability">Language-mode restriction, or null when no restriction applies.</param>
public sealed record PrologBuiltinDocumentation(string Signature, string Description, string? Availability);
