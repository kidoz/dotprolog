using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Extracts source definitions using the same module and include ownership model as linting.</summary>
public static class PrologSourceSymbols
{
    /// <summary>Returns definitions in supplied source order, without executing directives or lowering DCGs.</summary>
    public static IReadOnlyList<PrologSourceSymbol> Analyze(
        IReadOnlyList<PrologLintSource> sources,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Analyze(PrologLintProgramModel.Build(sources), cancellationToken);
    }

    internal static IReadOnlyList<PrologSourceSymbol> Analyze(PrologLintProgramModel model, CancellationToken cancellationToken)
    {
        List<PrologSourceSymbol> result = [];
        foreach (var context in model.Clauses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The lint model also retains initialization goals, which are not definitions.
            if (context.Clause is CompoundTerm { Name: ":-", Arity: 1 })
                continue;
            SyntaxTerm head = LintSyntax.ClauseHead(context.Clause);
            SourceSpan selection = head is CompoundTerm { NameSpan: { } name } ? name : head.Span;
            result.Add(
                new PrologSourceSymbol(
                    context.Source.FileName,
                    context.Module,
                    context.Indicator.Name,
                    context.Indicator.Arity - (context.Grammar ? 2 : 0),
                    context.Grammar,
                    context.Clause.Span,
                    selection
                )
            );
        }
        return result;
    }
}
