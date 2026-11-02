using System.Text;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports opt-in predicate and nonterminal names outside lower snake_case.</summary>
internal static class PrologNamingLinter
{
    internal static void Analyze(
        IReadOnlyList<SyntaxTerm> clauses,
        string? fileName,
        PrologLintOptions? options,
        List<Diagnostic> diagnostics
    )
    {
        if (options?.Enables(LintDiagnosticIds.NonSnakeCasePredicate) != true)
        {
            return;
        }

        var module = ModuleTable.UserModule;
        HashSet<(string Module, PredicateIndicator Indicator)> seen = [];
        foreach (SyntaxTerm clause in clauses)
        {
            if (clause is CompoundTerm { Name: ":-", Arity: 1, Arguments: [CompoundTerm directive] })
            {
                module = NextModule(directive, module);
                continue;
            }

            AnalyzeDefinition(clause, module, fileName, seen, diagnostics);
        }
    }

    internal static void Analyze(PrologLintProgramModel model, PrologLintOptions? options, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (options?.Enables(LintDiagnosticIds.NonSnakeCasePredicate) != true)
        {
            return;
        }

        HashSet<(string Module, PredicateIndicator Indicator)> seen = [];
        foreach (PrologLintProgramModel.ClauseContext clause in model.Clauses)
        {
            AnalyzeDefinition(clause.Clause, clause.Module, clause.Source.FileName, seen, diagnostics);
        }
    }

    private static void AnalyzeDefinition(
        SyntaxTerm clause,
        string module,
        string? fileName,
        HashSet<(string Module, PredicateIndicator Indicator)> seen,
        List<Diagnostic> diagnostics
    )
    {
        var grammar = clause is CompoundTerm { Name: "-->", Arity: 2 };
        SyntaxTerm head = LintSyntax.ClauseHead(clause);
        if (
            !LintSyntax.TryPredicate(head, grammar, out PredicateIndicator indicator)
            || !seen.Add((module, indicator))
            || !ShouldReport(indicator.Name)
        )
        {
            return;
        }

        SourceSpan nameSpan = head is CompoundTerm { NameSpan: { } span } ? span : head.Span;
        diagnostics.Add(
            new Diagnostic(
                LintDiagnosticIds.NonSnakeCasePredicate,
                DiagnosticSeverity.Warning,
                $"Name of {(grammar ? "nonterminal" : "predicate")} {module}:{LintSyntax.Display(indicator, grammar)} does not follow lower snake_case; review whether the deviation is intentional.",
                nameSpan,
                fileName
            )
        );
    }

    private static bool ShouldReport(string name)
    {
        if (name.StartsWith('$') || IsSymbolic(name))
        {
            return false;
        }

        return !IsLowerSnakeCase(name);
    }

    private static bool IsSymbolic(string name)
    {
        foreach (Rune rune in name.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowerSnakeCase(string name)
    {
        if (name.Length == 0 || name[0] is not (>= 'a' and <= 'z'))
        {
            return false;
        }

        for (var index = 1; index < name.Length; index++)
        {
            char current = name[index];
            if (current is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                continue;
            }

            if (current != '_' || index == name.Length - 1 || name[index - 1] == '_')
            {
                return false;
            }
        }

        return true;
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
