using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports opt-in local procedures that share predefined predicate identities.</summary>
internal static class PrologBuiltinShadowLinter
{
    internal static void Analyze(
        IReadOnlyList<SyntaxTerm> clauses,
        string? fileName,
        PrologLintOptions? options,
        List<Diagnostic> diagnostics
    )
    {
        ArgumentNullException.ThrowIfNull(clauses);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (options?.Enables(LintDiagnosticIds.BuiltinShadow) != true)
        {
            return;
        }

        Dictionary<(string Module, PredicateIndicator Indicator), Candidate> candidates = [];
        var module = ModuleTable.UserModule;
        foreach (SyntaxTerm clause in clauses)
        {
            if (clause is CompoundTerm { Name: ":-", Arity: 1, Arguments: [SyntaxTerm directiveTerm] })
            {
                if (directiveTerm is CompoundTerm directive)
                {
                    module = NextModule(directive, module);
                    if (directive is { Name: "dynamic" or "multifile", Arity: 1 })
                    {
                        AddDeclarations(directive.Arguments[0], module, fileName, candidates);
                    }
                }

                continue;
            }

            AddClause(clause, module, fileName, candidates);
        }

        Emit(candidates, diagnostics);
    }

    internal static void Analyze(PrologLintProgramModel model, PrologLintOptions? options, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (options?.Enables(LintDiagnosticIds.BuiltinShadow) != true)
        {
            return;
        }

        Dictionary<(string Module, PredicateIndicator Indicator), Candidate> candidates = [];
        foreach (PrologLintProgramModel.ClauseContext clause in model.Clauses)
        {
            if (clause.Clause is not CompoundTerm { Name: ":-", Arity: 1 })
            {
                AddClause(clause.Clause, clause.Module, clause.Source.FileName, candidates);
            }
        }

        foreach (PrologLintProgramModel.DeclarationSite declaration in model.LocalProcedureDeclarations)
        {
            AddDeclaration(
                declaration.Indicator,
                declaration.Nonterminal,
                declaration.Span,
                declaration.Module,
                declaration.FileName,
                candidates
            );
        }

        Emit(candidates, diagnostics);
    }

    private static void AddDeclarations(
        SyntaxTerm declarations,
        string module,
        string? fileName,
        Dictionary<(string Module, PredicateIndicator Indicator), Candidate> candidates
    )
    {
        foreach (SyntaxTerm item in LintSyntax.SequenceItems(declarations))
        {
            if (LintSyntax.TryIndicator(item, out PredicateIndicator indicator, out bool nonterminal))
            {
                AddDeclaration(indicator, nonterminal, item.Span, module, fileName, candidates);
            }
        }
    }

    private static void AddDeclaration(
        PredicateIndicator indicator,
        bool nonterminal,
        SourceSpan span,
        string module,
        string? fileName,
        Dictionary<(string Module, PredicateIndicator Indicator), Candidate> candidates
    )
    {
        Candidate? candidate = CandidateFor(module, indicator, candidates);
        if (candidate is not null && candidate.DeclarationSpan is null)
        {
            candidate.DeclarationSpan = span;
            candidate.DeclarationNonterminal = nonterminal;
            candidate.DeclarationFileName = fileName;
        }
    }

    private static void AddClause(
        SyntaxTerm clause,
        string module,
        string? fileName,
        Dictionary<(string Module, PredicateIndicator Indicator), Candidate> candidates
    )
    {
        var grammar = clause is CompoundTerm { Name: "-->", Arity: 2 };
        SyntaxTerm head = LintSyntax.ClauseHead(clause);
        if (!LintSyntax.TryPredicate(head, grammar, out PredicateIndicator indicator))
        {
            return;
        }

        Candidate? candidate = CandidateFor(module, indicator, candidates);
        if (candidate is not null && candidate.ClauseSpan is null)
        {
            candidate.ClauseSpan = head is CompoundTerm { NameSpan: { } nameSpan } ? nameSpan : head.Span;
            candidate.ClauseNonterminal = grammar;
            candidate.ClauseFileName = fileName;
        }
    }

    private static Candidate? CandidateFor(
        string module,
        PredicateIndicator indicator,
        Dictionary<(string Module, PredicateIndicator Indicator), Candidate> candidates
    )
    {
        if (!LintBuiltinCatalog.Contains(indicator))
        {
            return null;
        }

        if (!candidates.TryGetValue((module, indicator), out Candidate? candidate))
        {
            candidate = new Candidate();
            candidates.Add((module, indicator), candidate);
        }

        return candidate;
    }

    private static void Emit(
        Dictionary<(string Module, PredicateIndicator Indicator), Candidate> candidates,
        List<Diagnostic> diagnostics
    )
    {
        foreach (((string module, PredicateIndicator indicator), Candidate candidate) in candidates)
        {
            bool nonterminal = candidate.ClauseSpan is not null ? candidate.ClauseNonterminal : candidate.DeclarationNonterminal;
            diagnostics.Add(
                new Diagnostic(
                    LintDiagnosticIds.BuiltinShadow,
                    DiagnosticSeverity.Warning,
                    $"Local procedure {LintSyntax.Display(indicator, nonterminal)} in module {module} shares the identity of predefined predicate {LintSyntax.Display(indicator)}; review which definition calls resolve to.",
                    candidate.ClauseSpan ?? candidate.DeclarationSpan!.Value,
                    candidate.ClauseFileName ?? candidate.DeclarationFileName
                )
            );
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

    private sealed class Candidate
    {
        internal SourceSpan? ClauseSpan { get; set; }
        internal bool ClauseNonterminal { get; set; }
        internal string? ClauseFileName { get; set; }
        internal SourceSpan? DeclarationSpan { get; set; }
        internal bool DeclarationNonterminal { get; set; }
        internal string? DeclarationFileName { get; set; }
    }
}
