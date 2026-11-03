using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports imports rejected because another module already supplied the same identity.</summary>
internal static class PrologConflictingImportLinter
{
    internal static void Analyze(PrologLintProgramModel model, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        foreach (PrologLintProgramModel.ImportConflict conflict in model.ImportConflicts)
        {
            diagnostics.Add(
                new Diagnostic(
                    LintDiagnosticIds.ConflictingImport,
                    DiagnosticSeverity.Warning,
                    $"Import of {LintSyntax.Display(conflict.Indicator, conflict.Nonterminal)} into module {conflict.Importer} from module {conflict.LaterModule} conflicts with an earlier import from module {conflict.EarlierModule}; the later binding is not resolved.",
                    conflict.Span,
                    conflict.Source.FileName
                )
            );
        }
    }
}
