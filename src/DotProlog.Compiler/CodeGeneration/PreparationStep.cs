namespace DotProlog.Compiler;

internal sealed record PreparationStep(int Directive, List<CompiledPredicate> Predicates);
