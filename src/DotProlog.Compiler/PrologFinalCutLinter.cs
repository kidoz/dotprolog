using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports opt-in same-scope cuts in final positions of last supplied static clauses.</summary>
internal static class PrologFinalCutLinter
{
    internal static void Analyze(
        IReadOnlyList<SyntaxTerm> clauses,
        string? fileName,
        PrologLintOptions? options,
        List<Diagnostic> diagnostics
    )
    {
        if (options?.Enables(LintDiagnosticIds.PossibleFinalCut) != true)
        {
            return;
        }

        var module = ModuleTable.UserModule;
        Dictionary<(string Module, PredicateIndicator Indicator), LocalClause> last = [];
        HashSet<(string Module, PredicateIndicator Indicator)> dynamicOrMultifile = [];
        foreach (SyntaxTerm clause in clauses)
        {
            if (clause is CompoundTerm { Name: ":-", Arity: 1, Arguments: [CompoundTerm directive] })
            {
                CollectDeclaration(directive, module, dynamicOrMultifile);
                module = NextModule(directive, module);
                continue;
            }

            if (TryRule(clause, out CompoundTerm rule, out PredicateIndicator indicator, out var grammar))
            {
                last[(module, indicator)] = new LocalClause(rule, indicator, grammar);
            }
            else if (LintSyntax.TryPredicate(LintSyntax.ClauseHead(clause), grammar: false, out PredicateIndicator fact))
            {
                last.Remove((module, fact));
            }
        }

        foreach (((string Module, PredicateIndicator Indicator) key, LocalClause clause) in last)
        {
            if (!dynamicOrMultifile.Contains(key))
            {
                AnalyzeFinalCuts(clause.Rule.Arguments[1], clause.Indicator, clause.Grammar, fileName, diagnostics);
            }
        }
    }

    internal static void Analyze(PrologLintProgramModel model, PrologLintOptions? options, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (options?.Enables(LintDiagnosticIds.PossibleFinalCut) != true)
        {
            return;
        }

        Dictionary<(string Module, PredicateIndicator Indicator), PrologLintProgramModel.ClauseContext> last = [];
        foreach (PrologLintProgramModel.ClauseContext clause in model.Clauses)
        {
            if (TryRule(clause.Clause, out _, out _, out _))
            {
                last[(clause.Module, clause.Indicator)] = clause;
            }
            else if (LintSyntax.TryPredicate(LintSyntax.ClauseHead(clause.Clause), clause.Grammar, out PredicateIndicator fact))
            {
                last.Remove((clause.Module, fact));
            }
        }

        foreach (((string Module, PredicateIndicator Indicator) key, PrologLintProgramModel.ClauseContext clause) in last)
        {
            if (!model.IsDynamicOrMultifile(key.Module, key.Indicator) && clause.Clause is CompoundTerm rule)
            {
                AnalyzeFinalCuts(rule.Arguments[1], clause.Indicator, clause.Grammar, clause.Source.FileName, diagnostics);
            }
        }
    }

    private static string NextModule(CompoundTerm directive, string module) =>
        (directive.Name, directive.Arity, directive.Arguments[0]) switch
        {
            ("module", 1 or 2, AtomTerm name) => name.Name,
            ("body", 1, AtomTerm name) => name.Name,
            ("end_module" or "end_body", 1, _) => ModuleTable.UserModule,
            _ => module,
        };

    private static void CollectDeclaration(
        CompoundTerm directive,
        string module,
        HashSet<(string Module, PredicateIndicator Indicator)> dynamicOrMultifile
    )
    {
        if (directive is not { Name: "dynamic" or "multifile", Arity: 1 })
        {
            return;
        }

        foreach (SyntaxTerm item in LintSyntax.SequenceItems(directive.Arguments[0]))
        {
            if (LintSyntax.TryIndicator(item, out PredicateIndicator indicator, out _))
            {
                dynamicOrMultifile.Add((module, indicator));
            }
        }
    }

    private static bool TryRule(SyntaxTerm clause, out CompoundTerm rule, out PredicateIndicator indicator, out bool grammar)
    {
        rule = null!;
        indicator = default;
        grammar = clause is CompoundTerm { Name: "-->", Arity: 2 };
        if (
            clause is not CompoundTerm { Name: ":-" or "-->", Arity: 2 } candidate
            || !LintSyntax.TryPredicate(LintSyntax.ClauseHead(candidate), grammar, out indicator)
        )
        {
            return false;
        }

        rule = candidate;
        return true;
    }

    private static void AnalyzeFinalCuts(
        SyntaxTerm body,
        PredicateIndicator indicator,
        bool grammar,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        switch (body)
        {
            case AtomTerm { Name: "!" } cut:
                diagnostics.Add(
                    new Diagnostic(
                        LintDiagnosticIds.PossibleFinalCut,
                        DiagnosticSeverity.Warning,
                        $"Cut in a final position of the last supplied clause for {(grammar ? "nonterminal" : "predicate")} {LintSyntax.Display(indicator, grammar)}; review whether committing the remaining choice points is intentional.",
                        cut.Span,
                        fileName
                    )
                );
                return;
            case CompoundTerm { Name: "{}", Arity: 1 } ordinary when grammar:
                AnalyzeFinalCuts(ordinary.Arguments[0], indicator, grammar: false, fileName, diagnostics);
                return;
            case CompoundTerm { Name: ",", Arity: 2 } conjunction:
                AnalyzeFinalCuts(conjunction.Arguments[1], indicator, grammar, fileName, diagnostics);
                return;
            case CompoundTerm { Name: ";", Arity: 2 } disjunction:
                if (disjunction.Arguments[0] is CompoundTerm { Name: "->" or "*->", Arity: 2 } ifConditional)
                {
                    AnalyzeFinalCuts(ifConditional.Arguments[1], indicator, grammar, fileName, diagnostics);
                }
                else
                {
                    AnalyzeFinalCuts(disjunction.Arguments[0], indicator, grammar, fileName, diagnostics);
                }

                AnalyzeFinalCuts(disjunction.Arguments[1], indicator, grammar, fileName, diagnostics);
                return;
            case CompoundTerm { Name: "->" or "*->", Arity: 2 } conditional:
                AnalyzeFinalCuts(conditional.Arguments[1], indicator, grammar, fileName, diagnostics);
                return;
        }
    }

    private sealed record LocalClause(CompoundTerm Rule, PredicateIndicator Indicator, bool Grammar);
}
