namespace DotProlog.Compiler;

internal sealed record RawPreparationStep(int Directive, List<(int Functor, int Entry)> Predicates);
