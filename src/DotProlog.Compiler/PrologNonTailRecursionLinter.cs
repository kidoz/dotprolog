using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports opt-in direct self-calls outside compiler tail positions.</summary>
internal static class PrologNonTailRecursionLinter
{
    internal static void Analyze(
        IReadOnlyList<SyntaxTerm> clauses,
        string? fileName,
        PrologLintOptions? options,
        List<Diagnostic> diagnostics
    )
    {
        if (options?.Enables(LintDiagnosticIds.PossibleNonTailRecursion) != true)
        {
            return;
        }

        var module = ModuleTable.UserModule;
        foreach (SyntaxTerm clause in clauses)
        {
            if (clause is CompoundTerm { Name: ":-", Arity: 1, Arguments: [CompoundTerm directive] })
            {
                module = NextModule(directive, module);
                continue;
            }

            AnalyzeClause(clause, module, fileName, diagnostics);
        }
    }

    internal static void Analyze(PrologLintProgramModel model, PrologLintOptions? options, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (options?.Enables(LintDiagnosticIds.PossibleNonTailRecursion) != true)
        {
            return;
        }

        foreach (PrologLintProgramModel.ClauseContext clause in model.Clauses)
        {
            AnalyzeClause(clause.Clause, clause.Module, clause.Source.FileName, diagnostics);
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

    private static void AnalyzeClause(SyntaxTerm clause, string module, string? fileName, List<Diagnostic> diagnostics)
    {
        var grammar = clause is CompoundTerm { Name: "-->", Arity: 2 };
        if (
            clause is not CompoundTerm { Name: ":-" or "-->", Arity: 2 } rule
            || !LintSyntax.TryPredicate(LintSyntax.ClauseHead(rule), grammar, out PredicateIndicator indicator)
        )
        {
            return;
        }

        PredicateIndicator sourceIndicator = grammar ? new PredicateIndicator(indicator.Name, indicator.Arity - 2) : indicator;
        AnalyzeGoals(rule.Arguments[1], module, module, sourceIndicator, grammar, isTail: true, fileName, diagnostics);
    }

    private static void AnalyzeGoals(
        SyntaxTerm goal,
        string module,
        string definitionModule,
        PredicateIndicator self,
        bool grammar,
        bool isTail,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        switch (goal)
        {
            case AtomTerm atom when atom.Name != "!":
                CheckSelfCall(
                    atom,
                    new PredicateIndicator(atom.Name, 0),
                    module,
                    definitionModule,
                    self,
                    grammar,
                    isTail,
                    fileName,
                    diagnostics
                );
                return;
            case CompoundTerm { Name: ":", Arity: 2 } qualified when qualified.Arguments[0] is AtomTerm target:
                AnalyzeGoals(qualified.Arguments[1], target.Name, definitionModule, self, grammar, isTail, fileName, diagnostics);
                return;
            case CompoundTerm { Name: "{}", Arity: 1 } ordinary when grammar:
                AnalyzeGoals(
                    ordinary.Arguments[0],
                    module,
                    definitionModule,
                    self,
                    grammar: false,
                    isTail,
                    fileName,
                    diagnostics
                );
                return;
            case CompoundTerm { Name: ",", Arity: 2 } conjunction:
                AnalyzeGoals(
                    conjunction.Arguments[0],
                    module,
                    definitionModule,
                    self,
                    grammar,
                    isTail: false,
                    fileName,
                    diagnostics
                );
                AnalyzeGoals(conjunction.Arguments[1], module, definitionModule, self, grammar, isTail, fileName, diagnostics);
                return;
            case CompoundTerm { Name: ";", Arity: 2 } disjunction:
                if (disjunction.Arguments[0] is CompoundTerm { Name: "->" or "*->", Arity: 2 } ifConditional)
                {
                    AnalyzeGoals(
                        ifConditional.Arguments[0],
                        module,
                        definitionModule,
                        self,
                        grammar,
                        isTail: false,
                        fileName,
                        diagnostics
                    );
                    AnalyzeGoals(
                        ifConditional.Arguments[1],
                        module,
                        definitionModule,
                        self,
                        grammar,
                        isTail,
                        fileName,
                        diagnostics
                    );
                }
                else
                {
                    AnalyzeGoals(
                        disjunction.Arguments[0],
                        module,
                        definitionModule,
                        self,
                        grammar,
                        isTail,
                        fileName,
                        diagnostics
                    );
                }

                AnalyzeGoals(disjunction.Arguments[1], module, definitionModule, self, grammar, isTail, fileName, diagnostics);
                return;
            case CompoundTerm { Name: "->" or "*->", Arity: 2 } conditional:
                AnalyzeGoals(
                    conditional.Arguments[0],
                    module,
                    definitionModule,
                    self,
                    grammar,
                    isTail: false,
                    fileName,
                    diagnostics
                );
                AnalyzeGoals(conditional.Arguments[1], module, definitionModule, self, grammar, isTail, fileName, diagnostics);
                return;
            case CompoundTerm { Name: "\\+" or "call" }:
            case CompoundTerm { Name: "phrase", Arity: 1 } when grammar:
            case CompoundTerm { Name: ".", Arity: 2 } when grammar:
                return;
            case CompoundTerm compound:
                CheckSelfCall(
                    compound,
                    new PredicateIndicator(compound.Name, compound.Arity),
                    module,
                    definitionModule,
                    self,
                    grammar,
                    isTail,
                    fileName,
                    diagnostics
                );
                return;
        }
    }

    private static void CheckSelfCall(
        SyntaxTerm site,
        PredicateIndicator called,
        string module,
        string definitionModule,
        PredicateIndicator self,
        bool grammar,
        bool isTail,
        string? fileName,
        List<Diagnostic> diagnostics
    )
    {
        if (isTail || called != self || module != definitionModule)
        {
            return;
        }

        diagnostics.Add(
            new Diagnostic(
                LintDiagnosticIds.PossibleNonTailRecursion,
                DiagnosticSeverity.Warning,
                $"Direct self-call to {(grammar ? "nonterminal" : "predicate")} {Display(self, grammar)} is not in a tail position; remaining control flow prevents last-call optimization.",
                site.Span,
                fileName
            )
        );
    }

    private static string Display(PredicateIndicator indicator, bool grammar) =>
        grammar ? $"{indicator.Name}//{indicator.Arity}" : LintSyntax.Display(indicator);
}
