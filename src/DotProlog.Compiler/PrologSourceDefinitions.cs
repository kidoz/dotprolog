using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Resolves source definitions at statically identifiable predicate occurrences without execution.</summary>
public static class PrologSourceDefinitions
{
    /// <summary>Finds calls and heads in the supplied project/include snapshot, preserving ambiguous occurrences.</summary>
    public static IReadOnlyList<PrologDefinitionSite> Analyze(
        IReadOnlyList<PrologLintSource> sources,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Analyze(PrologLintProgramModel.Build(sources), cancellationToken);
    }

    internal static IReadOnlyList<PrologDefinitionSite> Analyze(PrologLintProgramModel model, CancellationToken cancellationToken)
    {
        var symbols = PrologSourceSymbols.Analyze(model, cancellationToken);
        var definitions = symbols
            .GroupBy(symbol => (symbol.Module, symbol.Name, Arity: symbol.Arity + (symbol.IsNonterminal ? 2 : 0)))
            .ToDictionary(group => group.Key, group => group.Distinct().ToArray());
        List<PrologDefinitionSite> result = [];
        var contexts = model
            .Clauses.Where(clause => clause.Clause is not CompoundTerm { Name: ":-", Arity: 1 })
            .Select(clause => model.ContextOf(clause.Clause))
            .ToArray();
        for (int index = 0; index < symbols.Count; index++)
        {
            var symbol = symbols[index];
            cancellationToken.ThrowIfCancellationRequested();
            int arity = symbol.Arity + (symbol.IsNonterminal ? 2 : 0);
            result.Add(
                new(
                    symbol.FileName,
                    symbol.SelectionRange,
                    symbol.Module,
                    symbol.Name,
                    arity,
                    definitions[(symbol.Module, symbol.Name, arity)],
                    contexts[index]
                )
                {
                    IsDefinition = true,
                }
            );
        }
        foreach (var site in PrologCallSites.Analyze(model, PrologLanguageMode.Modern, cancellationToken))
        {
            var targets = site.DefinitionModule is { } module
                ? definitions.GetValueOrDefault((module, site.Predicate.Name, site.Predicate.Arity)) ?? []
                : [];
            result.Add(
                new(
                    site.FileName,
                    site.Range,
                    site.DefinitionModule,
                    site.Predicate.Name,
                    site.Predicate.Arity,
                    targets,
                    site.Context
                )
            );
        }
        return result;
    }
}
