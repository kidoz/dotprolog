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
            if (
                _model.HasUnresolvedImports
                || _model.Sources.Any(source => source.HasAnalysisGaps)
                || _model.Clauses.Any(DefinesSourceExpansion)
            )
            {
                Incomplete = true;
                return;
            }

            foreach (PrologLintSource source in _model.Sources)
            {
                foreach (SyntaxTerm clause in source.Clauses)
                {
                    if (
                        clause is CompoundTerm { Name: ":-", Arity: 1, Arguments: [SyntaxTerm directive] }
                        && !IsDeclarativeDirective(directive)
                    )
                    {
                        Incomplete = true;
                    }
                }
            }

            if (Incomplete)
            {
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
                switch (clause.Clause)
                {
                    case CompoundTerm { Name: ":-", Arity: 2 } rule:
                        WalkGoal(rule.Arguments[1], clause.Module, new PredicateNode(clause.Module, clause.Indicator));
                        break;
                    case CompoundTerm { Name: "-->", Arity: 2 } grammar:
                        WalkGrammarBody(grammar.Arguments[1], clause.Module, new PredicateNode(clause.Module, clause.Indicator));
                        break;
                    case CompoundTerm { Name: ":-", Arity: 1, Arguments: [CompoundTerm { Name: "initialization" } directive] }:
                        WalkGoal(directive.Arguments[0], clause.Module, null);
                        break;
                }
            }
        }

        private static bool DefinesSourceExpansion(PrologLintProgramModel.ClauseContext clause) =>
            clause.Indicator is { Arity: 2, Name: "term_expansion" or "goal_expansion" };

        private static bool IsDeclarativeDirective(SyntaxTerm directive) =>
            directive is CompoundTerm compound
            && (compound.Name, compound.Arity) switch
            {
                ("module", 1 or 2) => true,
                ("export", 1) => true,
                ("use_module", 1 or 2) => true,
                ("import", 1 or 2) => true,
                ("reexport", 1 or 2) => true,
                ("dynamic", 1) => true,
                ("multifile", 1) => true,
                ("discontiguous", 1) => true,
                ("meta_predicate", 1) => true,
                ("metapredicate", 1) => true,
                ("end_module", 1) => true,
                ("body", 1) => true,
                ("end_body", 1) => true,
                ("op", 3) => true,
                ("set_prolog_flag", 2) => true,
                ("char_conversion", 2) => true,
                ("include", 1) => true,
                ("initialization", 1 or 2) => true,
                _ => false,
            };

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

        private void WalkGoal(SyntaxTerm term, string module, PredicateNode? source)
        {
            switch (term)
            {
                case VariableTerm:
                    Incomplete = true;
                    return;
                case AtomTerm atom:
                    if (atom.Name is not ("!" or "true" or "fail" or "false" or "[]"))
                    {
                        AddOrdinaryCall(new PredicateIndicator(atom.Name, 0), module, source);
                    }

                    return;
                case CompoundTerm { Name: ":", Arity: 2 } qualified:
                    if (qualified.Arguments[0] is AtomTerm target)
                    {
                        WalkGoal(qualified.Arguments[1], target.Name, source);
                    }
                    else
                    {
                        Incomplete = true;
                    }

                    return;
                case CompoundTerm goal when goal.Arity == 2 && goal.Name is "," or ";" or "|" or "->" or "*->":
                    WalkGoal(goal.Arguments[0], module, source);
                    WalkGoal(goal.Arguments[1], module, source);
                    return;
                case CompoundTerm { Name: "\\+", Arity: 1 } negation:
                    WalkGoal(negation.Arguments[0], module, source);
                    return;
                case CompoundTerm { Name: "^", Arity: 2 } existential:
                    WalkGoal(existential.Arguments[1], module, source);
                    return;
                case CompoundTerm goal:
                    if (ChangesProgramOrLoadsSource(goal))
                    {
                        Incomplete = true;
                        return;
                    }

                    AddOrdinaryCall(new PredicateIndicator(goal.Name, goal.Arity), module, source);
                    if (goal.Name == "phrase" && goal.Arity is 2 or 3)
                    {
                        WalkNonterminal(goal.Arguments[0], module, source);
                        return;
                    }

                    WalkMetaArguments(goal, module, source);
                    return;
            }
        }

        private static bool ChangesProgramOrLoadsSource(CompoundTerm goal) =>
            goal.Name
                is "assert"
                    or "asserta"
                    or "assertz"
                    or "retract"
                    or "retractall"
                    or "abolish"
                    or "consult"
                    or "ensure_loaded"
                    or "load_files";

        private void WalkMetaArguments(CompoundTerm goal, string module, PredicateNode? source)
        {
            int[]? meta = _model.MetaArgumentsOf(module, new PredicateIndicator(goal.Name, goal.Arity));
            if (meta is null)
            {
                return;
            }

            for (var index = 0; index < meta.Length; index++)
            {
                if (meta[index] == 0)
                {
                    WalkGoal(goal.Arguments[index], module, source);
                }
                else if (meta[index] > 0)
                {
                    WalkClosure(goal.Arguments[index], meta[index], module, source);
                }
            }
        }

        private void WalkClosure(SyntaxTerm term, int extraArguments, string module, PredicateNode? source)
        {
            switch (term)
            {
                case VariableTerm:
                    Incomplete = true;
                    return;
                case AtomTerm atom:
                    AddOrdinaryCall(new PredicateIndicator(atom.Name, extraArguments), module, source);
                    return;
                case CompoundTerm { Name: ":", Arity: 2 } qualified when qualified.Arguments[0] is AtomTerm target:
                    WalkClosure(qualified.Arguments[1], extraArguments, target.Name, source);
                    return;
                case CompoundTerm compound:
                    AddOrdinaryCall(new PredicateIndicator(compound.Name, compound.Arity + extraArguments), module, source);
                    return;
                default:
                    Incomplete = true;
                    return;
            }
        }

        private void WalkGrammarBody(SyntaxTerm term, string module, PredicateNode source)
        {
            switch (term)
            {
                case VariableTerm:
                    Incomplete = true;
                    return;
                case StringTerm:
                    return;
                case CompoundTerm { Name: "{}", Arity: 1 } ordinary:
                    WalkGoal(ordinary.Arguments[0], module, source);
                    return;
                case CompoundTerm { Name: ".", Arity: 2 }:
                case AtomTerm { Name: "!" or "[]" }:
                    return;
                case AtomTerm atom:
                    AddNonterminalCall(new PredicateIndicator(atom.Name, 0), module, source);
                    return;
                case CompoundTerm goal when goal.Arity == 2 && goal.Name is "," or ";" or "|" or "->" or "*->":
                    WalkGrammarBody(goal.Arguments[0], module, source);
                    WalkGrammarBody(goal.Arguments[1], module, source);
                    return;
                case CompoundTerm { Name: "\\+", Arity: 1 } negation:
                    WalkGrammarBody(negation.Arguments[0], module, source);
                    return;
                case CompoundTerm { Name: "call", Arity: 1 } call:
                    WalkClosure(call.Arguments[0], 2, module, source);
                    return;
                case CompoundTerm { Name: "phrase", Arity: 1 } phrase:
                    WalkNonterminal(phrase.Arguments[0], module, source);
                    return;
                case CompoundTerm nonterminal:
                    AddNonterminalCall(new PredicateIndicator(nonterminal.Name, nonterminal.Arity), module, source);
                    return;
                default:
                    Incomplete = true;
                    return;
            }
        }

        private void WalkNonterminal(SyntaxTerm term, string module, PredicateNode? source)
        {
            switch (term)
            {
                case VariableTerm:
                    Incomplete = true;
                    return;
                case AtomTerm atom:
                    AddNonterminalCall(new PredicateIndicator(atom.Name, 0), module, source);
                    return;
                case CompoundTerm { Name: ":", Arity: 2 } qualified when qualified.Arguments[0] is AtomTerm target:
                    WalkNonterminal(qualified.Arguments[1], target.Name, source);
                    return;
                case CompoundTerm compound:
                    AddNonterminalCall(new PredicateIndicator(compound.Name, compound.Arity), module, source);
                    return;
                default:
                    Incomplete = true;
                    return;
            }
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
