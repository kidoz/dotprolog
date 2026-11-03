using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports opt-in imports hidden by local predicate or nonterminal definitions.</summary>
internal static class PrologLocalImportShadowLinter
{
    internal static void Analyze(PrologLintProgramModel model, PrologLintOptions? options, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (options?.Enables(LintDiagnosticIds.LocalImportShadow) != true)
        {
            return;
        }

        foreach (PrologLintProgramModel.LocalImportShadow shadow in model.LocalImportShadows)
        {
            PrologLintProgramModel.ClauseContext? clause = model.Clauses.FirstOrDefault(clause =>
                clause.Module == shadow.Importer && clause.Indicator == shadow.Indicator
            );
            SourceSpan span;
            string fileName;
            if (clause is not null)
            {
                SyntaxTerm head = LintSyntax.ClauseHead(clause.Clause);
                span = head is CompoundTerm { NameSpan: { } nameSpan } ? nameSpan : head.Span;
                fileName = clause.Source.FileName;
            }
            else
            {
                PrologLintProgramModel.DeclarationSite? declaration = model.LocalProcedureDeclarations.FirstOrDefault(
                    declaration => declaration.Module == shadow.Importer && declaration.Indicator == shadow.Indicator
                );
                if (declaration is null)
                {
                    continue;
                }

                span = declaration.Span;
                fileName = declaration.FileName;
            }

            diagnostics.Add(
                new Diagnostic(
                    LintDiagnosticIds.LocalImportShadow,
                    DiagnosticSeverity.Warning,
                    $"Local procedure {LintSyntax.Display(shadow.Indicator, shadow.Nonterminal)} in module {shadow.Importer} shadows the import from module {shadow.SourceModule}; the imported binding is not resolved.",
                    span,
                    fileName
                )
            );
        }
    }
}
