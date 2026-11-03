using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports conflicting mode signatures declared for one module and predicate.</summary>
internal static class PrologMetaPredicateDeclarationLinter
{
    internal static void Analyze(IReadOnlyList<SyntaxTerm> clauses, string? fileName, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(clauses);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Dictionary<(string Module, PredicateIndicator Indicator), DeclarationHistory> declarations = [];
        var module = ModuleTable.UserModule;
        foreach (SyntaxTerm clause in clauses)
        {
            if (clause is not CompoundTerm { Name: ":-", Arity: 1, Arguments: [CompoundTerm directive] })
            {
                continue;
            }

            module = NextModule(directive, module);
            if (directive is not { Name: "meta_predicate" or "metapredicate", Arity: 1 })
            {
                continue;
            }

            foreach (SyntaxTerm item in LintSyntax.SequenceItems(directive.Arguments[0]))
            {
                if (
                    PrologMetaPredicateDeclarationSyntax.TryNormalize(
                        directive.Name,
                        item,
                        out PredicateIndicator indicator,
                        out string template,
                        out string signature
                    )
                )
                {
                    AddDeclaration(
                        new PrologLintProgramModel.MetaPredicateDeclaration(
                            module,
                            indicator,
                            template,
                            signature,
                            item.Span,
                            fileName
                        ),
                        declarations,
                        diagnostics
                    );
                }
            }
        }
    }

    internal static void Analyze(PrologLintProgramModel model, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Dictionary<(string Module, PredicateIndicator Indicator), DeclarationHistory> declarations = [];
        foreach (PrologLintProgramModel.MetaPredicateDeclaration declaration in model.MetaPredicateDeclarations)
        {
            AddDeclaration(declaration, declarations, diagnostics);
        }
    }

    private static void AddDeclaration(
        PrologLintProgramModel.MetaPredicateDeclaration declaration,
        Dictionary<(string Module, PredicateIndicator Indicator), DeclarationHistory> declarations,
        List<Diagnostic> diagnostics
    )
    {
        var key = (declaration.Module, declaration.Indicator);
        if (!declarations.TryGetValue(key, out DeclarationHistory? history))
        {
            declarations.Add(key, new DeclarationHistory(declaration.Signature, declaration.Template));
            return;
        }

        if (history.Reported || history.Signature == declaration.Signature)
        {
            return;
        }

        diagnostics.Add(
            new Diagnostic(
                LintDiagnosticIds.InconsistentMetaPredicate,
                DiagnosticSeverity.Warning,
                $"Meta-predicate declaration {declaration.Template} for {declaration.Module}:{LintSyntax.Display(declaration.Indicator)} conflicts with earlier template {history.Template}; the argument modes disagree.",
                declaration.Span,
                declaration.FileName
            )
        );
        history.Reported = true;
    }

    private static string NextModule(CompoundTerm directive, string module) =>
        (directive.Name, directive.Arity, directive.Arguments[0]) switch
        {
            ("module", 1 or 2, AtomTerm name) => name.Name,
            ("body", 1, AtomTerm name) => name.Name,
            ("end_module" or "end_body", 1, _) => ModuleTable.UserModule,
            _ => module,
        };

    private sealed class DeclarationHistory(string signature, string template)
    {
        internal string Signature { get; } = signature;
        internal string Template { get; } = template;
        internal bool Reported { get; set; }
    }
}
