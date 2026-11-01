using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports imported bindings without statically resolved references in the importing module.</summary>
internal static class PrologUnusedImportLinter
{
    internal static void Analyze(PrologLintProgramModel model, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (PrologLintCompleteness.HasProgramWideGaps(model))
        {
            return;
        }

        var analysis = new ImportUsageAnalysis(model);
        analysis.Build();
        if (analysis.Incomplete)
        {
            return;
        }

        foreach (PrologLintProgramModel.ImportSite import in model.Imports)
        {
            if (import.Reexport || import.SourceModule is not { } sourceModule || model.HasNonPredicateEffects(sourceModule))
            {
                continue;
            }

            if (import.Selective)
            {
                foreach (PrologLintProgramModel.ImportBinding binding in import.Bindings)
                {
                    UsageKey key = new(import.Importer, sourceModule, binding.Indicator);
                    if (analysis.Used.Contains(key))
                    {
                        continue;
                    }

                    string display = LintSyntax.Display(binding.Indicator, binding.Nonterminal);
                    diagnostics.Add(
                        new Diagnostic(
                            LintDiagnosticIds.PossiblyUnusedImport,
                            DiagnosticSeverity.Warning,
                            $"No static reference in module {import.Importer} uses imported {display} from module {sourceModule}; the import may be unused.",
                            binding.Span,
                            import.Source.FileName
                        )
                    );
                }

                continue;
            }

            if (analysis.Used.Any(usage => usage.Importer == import.Importer && usage.SourceModule == sourceModule))
            {
                continue;
            }

            diagnostics.Add(
                new Diagnostic(
                    LintDiagnosticIds.PossiblyUnusedImport,
                    DiagnosticSeverity.Warning,
                    $"No static reference in module {import.Importer} uses any predicate or nonterminal imported from module {sourceModule}; the import may be unused.",
                    import.SourceTerm.Span,
                    import.Source.FileName
                )
            );
        }
    }

    private readonly record struct UsageKey(string Importer, string SourceModule, PredicateIndicator Predicate);

    private sealed class ImportUsageAnalysis
    {
        private readonly PrologLintProgramModel _model;

        internal ImportUsageAnalysis(PrologLintProgramModel model) => _model = model;

        internal bool Incomplete { get; private set; }
        internal HashSet<UsageKey> Used { get; } = [];

        internal void Build()
        {
            foreach (PrologLintProgramModel.ClauseContext clause in _model.Clauses)
            {
                var walker = new PrologLintCallWalker(_model, AddOrdinaryCall, AddNonterminalCall);
                switch (clause.Clause)
                {
                    case CompoundTerm { Name: ":-", Arity: 2 } rule:
                        walker.WalkGoal(rule.Arguments[1], clause.Module);
                        break;
                    case CompoundTerm { Name: "-->", Arity: 2 } grammar:
                        walker.WalkGrammarBody(grammar.Arguments[1], clause.Module);
                        break;
                    case CompoundTerm { Name: ":-", Arity: 1, Arguments: [CompoundTerm { Name: "initialization" } directive] }:
                        walker.WalkGoal(directive.Arguments[0], clause.Module);
                        break;
                }

                Incomplete |= walker.Incomplete;
            }
        }

        private void AddOrdinaryCall(PredicateIndicator predicate, string module, bool explicitlyQualified)
        {
            PrologLintProgramModel.LintResolution resolution = _model.Resolve(module, predicate);
            if (!resolution.Known || resolution.IsUnknown)
            {
                Incomplete = true;
                return;
            }

            if (
                !explicitlyQualified
                && resolution.Module is { } target
                && _model.IsOrdinary(target, predicate)
                && _model.Modules.ImportedFrom(module, predicate) is { } imported
            )
            {
                Used.Add(new UsageKey(module, imported, predicate));
            }
        }

        private void AddNonterminalCall(PredicateIndicator sourceIndicator, string module, bool explicitlyQualified)
        {
            PredicateIndicator compiled = new(sourceIndicator.Name, sourceIndicator.Arity + 2);
            PrologLintProgramModel.LintResolution resolution = _model.Resolve(module, compiled);
            if (!resolution.Known || resolution.IsUnknown)
            {
                Incomplete = true;
                return;
            }

            if (
                !explicitlyQualified
                && resolution.Module is { } target
                && _model.IsNonterminal(target, compiled)
                && _model.Modules.ImportedFrom(module, compiled) is { } imported
            )
            {
                Used.Add(new UsageKey(module, imported, compiled));
            }
        }
    }
}
