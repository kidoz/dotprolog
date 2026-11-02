using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports opt-in predicate and nonterminal definitions above a source-arity limit.</summary>
internal static class PrologExcessiveArityLinter
{
    internal static void Analyze(
        IReadOnlyList<SyntaxTerm> clauses,
        string? fileName,
        PrologLintOptions? options,
        List<Diagnostic> diagnostics
    )
    {
        if (options?.Enables(LintDiagnosticIds.ExcessivePredicateArity) != true)
        {
            return;
        }

        int maximumArity = MaximumArity(options);
        var module = ModuleTable.UserModule;
        HashSet<(string Module, PredicateIndicator Indicator)> seen = [];
        foreach (SyntaxTerm clause in clauses)
        {
            if (clause is CompoundTerm { Name: ":-", Arity: 1, Arguments: [CompoundTerm directive] })
            {
                module = NextModule(directive, module);
                continue;
            }

            AnalyzeDefinition(clause, module, maximumArity, fileName, seen, diagnostics);
        }
    }

    internal static void Analyze(PrologLintProgramModel model, PrologLintOptions? options, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (options?.Enables(LintDiagnosticIds.ExcessivePredicateArity) != true)
        {
            return;
        }

        int maximumArity = MaximumArity(options);
        HashSet<(string Module, PredicateIndicator Indicator)> seen = [];
        foreach (PrologLintProgramModel.ClauseContext clause in model.Clauses)
        {
            AnalyzeDefinition(clause.Clause, clause.Module, maximumArity, clause.Source.FileName, seen, diagnostics);
        }
    }

    private static int MaximumArity(PrologLintOptions options)
    {
        if (options.MaxPredicateArity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxPredicateArity,
                "Maximum predicate arity must be positive."
            );
        }

        return options.MaxPredicateArity;
    }

    private static void AnalyzeDefinition(
        SyntaxTerm clause,
        string module,
        int maximumArity,
        string? fileName,
        HashSet<(string Module, PredicateIndicator Indicator)> seen,
        List<Diagnostic> diagnostics
    )
    {
        var grammar = clause is CompoundTerm { Name: "-->", Arity: 2 };
        SyntaxTerm head = LintSyntax.ClauseHead(clause);
        if (!LintSyntax.TryPredicate(head, grammar, out PredicateIndicator indicator) || !seen.Add((module, indicator)))
        {
            return;
        }

        int sourceArity = grammar ? indicator.Arity - 2 : indicator.Arity;
        if (sourceArity <= maximumArity)
        {
            return;
        }

        SourceSpan nameSpan = head is CompoundTerm { NameSpan: { } span } ? span : head.Span;
        diagnostics.Add(
            new Diagnostic(
                LintDiagnosticIds.ExcessivePredicateArity,
                DiagnosticSeverity.Warning,
                $"{(grammar ? "Nonterminal" : "Predicate")} {module}:{LintSyntax.Display(indicator, grammar)} has {sourceArity} arguments, exceeding the configured maximum of {maximumArity}; review whether the argument-list design is intentional.",
                nameSpan,
                fileName
            )
        );
    }

    private static string NextModule(CompoundTerm directive, string module) =>
        (directive.Name, directive.Arity, directive.Arguments[0]) switch
        {
            ("module", 1 or 2, AtomTerm name) => name.Name,
            ("body", 1, AtomTerm name) => name.Name,
            ("end_module" or "end_body", 1, _) => ModuleTable.UserModule,
            _ => module,
        };
}
