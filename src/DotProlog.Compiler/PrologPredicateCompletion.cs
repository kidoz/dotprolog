namespace DotProlog.Compiler;

/// <summary>A visible predicate candidate from non-executing source analysis.</summary>
/// <param name="Name">The unqualified predicate name.</param>
/// <param name="Arity">The callable arity, including DCG expansion arguments.</param>
/// <param name="Module">The defining module, or null for a predefined predicate.</param>
/// <param name="Documentation">Builtin help when the candidate resolves to a predefined predicate.</param>
public sealed record PrologPredicateCompletion(string Name, int Arity, string? Module, PrologBuiltinDocumentation? Documentation);
