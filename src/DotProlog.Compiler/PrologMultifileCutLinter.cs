using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports opt-in same-scope cuts in clauses declared multifile.</summary>
internal static class PrologMultifileCutLinter
{
    internal static void Analyze(
        IReadOnlyList<SyntaxTerm> clauses,
        string? fileName,
        PrologLintOptions? options,
        List<Diagnostic> diagnostics
    )
    {
        if (options?.Enables(LintDiagnosticIds.PossibleMultifileCut) != true)
        {
            return;
        }

        HashSet<PredicateIndicator> multifile = LintDeclarations.MultifileIndicators(clauses);
        foreach (SyntaxTerm clause in clauses)
        {
            AnalyzeClause(clause, multifile.Contains, fileName, diagnostics);
        }
    }

    internal static void Analyze(PrologLintProgramModel model, PrologLintOptions? options, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (options?.Enables(LintDiagnosticIds.PossibleMultifileCut) != true)
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
            || !isMultifile(indicator)
            || LintCutScope.FirstClauseCut(rule.Arguments[1], grammar) is not { } cut
        )
        {
            return;
        }

        diagnostics.Add(
            new Diagnostic(
                LintDiagnosticIds.PossibleMultifileCut,
                DiagnosticSeverity.Warning,
                $"Cut in multifile {(grammar ? "nonterminal" : "predicate")} {LintSyntax.Display(indicator, grammar)} may discard alternatives contributed by other source units.",
                cut,
                fileName
            )
        );
    }
}
