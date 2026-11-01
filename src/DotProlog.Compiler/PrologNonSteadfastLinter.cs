using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports opt-in clauses with cross-argument head aliases and a same-scope cut.</summary>
internal static class PrologNonSteadfastLinter
{
    internal static void Analyze(
        IReadOnlyList<SyntaxTerm> clauses,
        string? fileName,
        PrologLintOptions? options,
        List<Diagnostic> diagnostics
    )
    {
        if (options?.Enables(LintDiagnosticIds.PossibleNonSteadfastClause) != true)
        {
            return;
        }

        HashSet<PredicateIndicator> multifile = MultifileIndicators(clauses);
        foreach (SyntaxTerm clause in clauses)
        {
            AnalyzeClause(clause, multifile.Contains, fileName, diagnostics);
        }
    }

    internal static void Analyze(PrologLintProgramModel model, PrologLintOptions? options, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (options?.Enables(LintDiagnosticIds.PossibleNonSteadfastClause) != true)
        {
            return;
        }

        foreach (PrologLintProgramModel.ClauseContext clause in model.Clauses)
        {
            AnalyzeClause(
                clause.Clause,
                indicator => model.Modules.IsMultifile(clause.Module, indicator),
                clause.Source.FileName,
                diagnostics
            );
        }
    }

    private static void AnalyzeClause(
        SyntaxTerm clause,
        Func<PredicateIndicator, bool> isMultifile,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        var grammar = clause is CompoundTerm { Name: "-->", Arity: 2 };
        if (
            clause is not CompoundTerm { Name: ":-" or "-->", Arity: 2 } rule
            || !LintSyntax.TryPredicate(LintSyntax.ClauseHead(rule), grammar, out PredicateIndicator indicator)
            || isMultifile(indicator)
            || !HasCrossArgumentAlias(LintSyntax.ClauseHead(rule))
            || FirstClauseCut(rule.Arguments[1], grammar) is not { } cut
        )
        {
            return;
        }

        diagnostics.Add(
            new Diagnostic(
                LintDiagnosticIds.PossibleNonSteadfastClause,
                DiagnosticSeverity.Warning,
                $"Clause for {(grammar ? "nonterminal" : "predicate")} {LintSyntax.Display(indicator, grammar)} may be non-steadfast because distinct head arguments share variables and the body contains a cut.",
                cut,
                fileName
            )
        );
    }

    private static HashSet<PredicateIndicator> MultifileIndicators(IReadOnlyList<SyntaxTerm> clauses)
    {
        HashSet<PredicateIndicator> result = [];
        foreach (
            CompoundTerm directive in clauses
                .OfType<CompoundTerm>()
                .Where(clause => clause is { Name: ":-", Arity: 1, Arguments: [CompoundTerm { Name: "multifile", Arity: 1 }] })
        )
        {
            CompoundTerm declaration = (CompoundTerm)directive.Arguments[0];
            foreach (SyntaxTerm item in LintSyntax.SequenceItems(declaration.Arguments[0]))
            {
                if (LintSyntax.TryIndicator(item, out PredicateIndicator indicator, out _))
                {
                    result.Add(indicator);
                }
            }
        }

        return result;
    }

    private static bool HasCrossArgumentAlias(SyntaxTerm head)
    {
        if (head is not CompoundTerm compound)
        {
            return false;
        }

        HashSet<string>[] variables = [.. compound.Arguments.Select(NamedVariables)];
        for (var left = 0; left < variables.Length; left++)
        {
            for (var right = left + 1; right < variables.Length; right++)
            {
                if (variables[left].Overlaps(variables[right]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static HashSet<string> NamedVariables(SyntaxTerm term) =>
        [.. LintSyntax.Variables(term).Where(variable => !variable.IsAnonymous).Select(variable => variable.Name)];

    private static SourceSpan? FirstClauseCut(SyntaxTerm body, bool grammar)
    {
        switch (body)
        {
            case AtomTerm { Name: "!" } cut:
                return cut.Span;
            case CompoundTerm { Name: "{}", Arity: 1 } ordinary when grammar:
                return FirstClauseCut(ordinary.Arguments[0], grammar: false);
            case CompoundTerm { Name: ",", Arity: 2 } conjunction:
                return FirstClauseCut(conjunction.Arguments[0], grammar) ?? FirstClauseCut(conjunction.Arguments[1], grammar);
            case CompoundTerm { Name: ";", Arity: 2 } disjunction:
                if (disjunction.Arguments[0] is CompoundTerm { Name: "->" or "*->", Arity: 2 } ifConditional)
                {
                    return FirstClauseCut(ifConditional.Arguments[1], grammar)
                        ?? FirstClauseCut(disjunction.Arguments[1], grammar);
                }

                return FirstClauseCut(disjunction.Arguments[0], grammar) ?? FirstClauseCut(disjunction.Arguments[1], grammar);
            case CompoundTerm { Name: "->", Arity: 2 } conditional:
                return FirstClauseCut(conditional.Arguments[1], grammar);
            case CompoundTerm { Name: "*->", Arity: 2 } softConditional:
                return FirstClauseCut(softConditional.Arguments[0], grammar)
                    ?? FirstClauseCut(softConditional.Arguments[1], grammar);
            default:
                return null;
        }
    }
}
