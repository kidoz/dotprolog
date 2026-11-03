using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports selective imports rejected because their source does not export the requested identity.</summary>
internal static class PrologMissingExportLinter
{
    internal static void Analyze(PrologLintProgramModel model, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        foreach (PrologLintProgramModel.MissingExport missing in model.MissingExports)
        {
            diagnostics.Add(
                new Diagnostic(
                    LintDiagnosticIds.MissingExport,
                    DiagnosticSeverity.Warning,
                    $"Import of {LintSyntax.Display(missing.Indicator, missing.Nonterminal)} into module {missing.Importer} from module {missing.SourceModule} is not exported by source module {missing.SourceModule}; the binding is not resolved.",
                    missing.Span,
                    missing.Source.FileName
                )
            );
        }
    }
}
