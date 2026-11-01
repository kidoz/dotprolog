using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports ordinary predicates unreachable from recognized static entry points.</summary>
internal static class PrologReachabilityLinter
{
    internal static void Analyze(PrologLintProgramModel model, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var analysis = new ReachabilityAnalysis(model);
        analysis.Build();
        if (analysis.Incomplete)
        {
            return;
        }

        HashSet<PredicateNode> reachable = [];
        Stack<PredicateNode> pending = [];
        foreach (PredicateNode root in analysis.Roots)
        {
            if (reachable.Add(root))
            {
                pending.Push(root);
            }
        }

        while (pending.TryPop(out PredicateNode current))
        {
            if (!analysis.Edges.TryGetValue(current, out HashSet<PredicateNode>? targets))
            {
                continue;
            }

            foreach (PredicateNode target in targets)
            {
                if (reachable.Add(target))
                {
                    pending.Push(target);
                }
            }
        }

        foreach (
            IGrouping<PredicateNode, PrologLintProgramModel.ClauseContext> definition in model
                .Clauses.Where(clause => !clause.Grammar && clause.Clause is not CompoundTerm { Name: ":-", Arity: 1 })
                .GroupBy(clause => new PredicateNode(clause.Module, clause.Indicator))
        )
        {
            if (reachable.Contains(definition.Key))
            {
                continue;
            }

            PrologLintProgramModel.ClauseContext first = definition.First();
            diagnostics.Add(
                new Diagnostic(
                    LintDiagnosticIds.PossibleUnreferencedPredicate,
                    DiagnosticSeverity.Warning,
                    $"No reachable static call references predicate {definition.Key.Module}:{LintSyntax.Display(definition.Key.Predicate)}; it may be unreferenced.",
                    LintSyntax.ClauseHead(first.Clause).Span,
                    first.Source.FileName
                )
            );
        }
    }

    private readonly record struct PredicateNode(string Module, PredicateIndicator Predicate);

    private sealed class ReachabilityAnalysis
    {
        private readonly PrologLintProgramModel _model;

        internal ReachabilityAnalysis(PrologLintProgramModel model) => _model = model;

        internal bool Incomplete { get; private set; }
        internal Dictionary<PredicateNode, HashSet<PredicateNode>> Edges { get; } = [];
        internal HashSet<PredicateNode> Roots { get; } = [];

        internal void Build()
        {
            if (PrologLintCompleteness.HasProgramWideGaps(_model))
            {
                Incomplete = true;
                return;
            }

            foreach (PrologLintProgramModel.ClauseContext clause in _model.Clauses)
            {
                if (clause.Clause is CompoundTerm { Name: ":-", Arity: 1 })
                {
                    continue;
                }

                PredicateNode node = new(clause.Module, clause.Indicator);
                if (
                    clause.Module == ModuleTable.UserModule
                    || _model.Modules.Exports(clause.Module, clause.Indicator)
                    || _model.IsDynamicOrMultifile(clause.Module, clause.Indicator)
                    || (
                        !clause.Grammar
                        && clause.Indicator.Arity == 0
                        && clause.Indicator.Name.StartsWith("test_", StringComparison.Ordinal)
                    )
                )
                {
                    Roots.Add(node);
                }
            }

            foreach (PrologLintProgramModel.ClauseContext clause in _model.Clauses)
            {
                PredicateNode? source = clause.Clause is CompoundTerm { Name: ":-", Arity: 1 }
                    ? null
                    : new PredicateNode(clause.Module, clause.Indicator);
                var walker = new PrologLintCallWalker(
                    _model,
                    (predicate, module, _) => AddOrdinaryCall(predicate, module, source),
                    (indicator, module, _) => AddNonterminalCall(indicator, module, source)
                );
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

        private void AddEdge(PredicateNode? source, PredicateNode target)
        {
            if (source is not { } from)
            {
                Roots.Add(target);
                return;
            }

            if (!Edges.TryGetValue(from, out HashSet<PredicateNode>? targets))
            {
                targets = [];
                Edges.Add(from, targets);
            }

            targets.Add(target);
        }

        private void AddOrdinaryCall(PredicateIndicator predicate, string module, PredicateNode? source)
        {
            PrologLintProgramModel.LintResolution resolution = _model.Resolve(module, predicate);
            if (!resolution.Known || resolution.IsUnknown)
            {
                Incomplete = true;
                return;
            }

            if (resolution.Module is { } target)
            {
                AddEdge(source, new PredicateNode(target, predicate));
            }
        }

        private void AddNonterminalCall(PredicateIndicator sourceIndicator, string module, PredicateNode? source)
        {
            PredicateIndicator compiled = new(sourceIndicator.Name, sourceIndicator.Arity + 2);
            PrologLintProgramModel.LintResolution resolution = _model.Resolve(module, compiled);
            if (resolution.Module is { } target && _model.IsNonterminal(target, compiled))
            {
                AddEdge(source, new PredicateNode(target, compiled));
                return;
            }

            Incomplete = true;
        }
    }
}
