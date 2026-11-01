using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Traverses statically visible calls and marks goal shapes that make analysis incomplete.</summary>
internal sealed class PrologLintCallWalker
{
    private readonly PrologLintProgramModel _model;
    private readonly Action<PredicateIndicator, string, bool> _ordinaryCall;
    private readonly Action<PredicateIndicator, string, bool> _nonterminalCall;

    internal PrologLintCallWalker(
        PrologLintProgramModel model,
        Action<PredicateIndicator, string, bool> ordinaryCall,
        Action<PredicateIndicator, string, bool> nonterminalCall
    )
    {
        _model = model;
        _ordinaryCall = ordinaryCall;
        _nonterminalCall = nonterminalCall;
    }

    internal bool Incomplete { get; private set; }

    internal void WalkGoal(SyntaxTerm term, string module, bool explicitlyQualified = false)
    {
        switch (term)
        {
            case VariableTerm:
                Incomplete = true;
                return;
            case AtomTerm atom:
                if (atom.Name is not ("!" or "true" or "fail" or "false" or "[]"))
                {
                    _ordinaryCall(new PredicateIndicator(atom.Name, 0), module, explicitlyQualified);
                }

                return;
            case CompoundTerm { Name: ":", Arity: 2 } qualified:
                if (qualified.Arguments[0] is AtomTerm target)
                {
                    WalkGoal(qualified.Arguments[1], target.Name, explicitlyQualified: true);
                }
                else
                {
                    Incomplete = true;
                }

                return;
            case CompoundTerm goal when goal.Arity == 2 && goal.Name is "," or ";" or "|" or "->" or "*->":
                WalkGoal(goal.Arguments[0], module, explicitlyQualified);
                WalkGoal(goal.Arguments[1], module, explicitlyQualified);
                return;
            case CompoundTerm { Name: "\\+", Arity: 1 } negation:
                WalkGoal(negation.Arguments[0], module, explicitlyQualified);
                return;
            case CompoundTerm { Name: "^", Arity: 2 } existential:
                WalkGoal(existential.Arguments[1], module, explicitlyQualified);
                return;
            case CompoundTerm goal:
                if (ChangesProgramOrLoadsSource(goal))
                {
                    Incomplete = true;
                    return;
                }

                _ordinaryCall(new PredicateIndicator(goal.Name, goal.Arity), module, explicitlyQualified);
                if (goal.Name == "phrase" && goal.Arity is 2 or 3)
                {
                    WalkNonterminal(goal.Arguments[0], module, explicitlyQualified);
                    return;
                }

                WalkMetaArguments(goal, module, explicitlyQualified);
                return;
        }
    }

    internal void WalkGrammarBody(SyntaxTerm term, string module, bool explicitlyQualified = false)
    {
        switch (term)
        {
            case VariableTerm:
                Incomplete = true;
                return;
            case StringTerm:
                return;
            case CompoundTerm { Name: "{}", Arity: 1 } ordinary:
                WalkGoal(ordinary.Arguments[0], module, explicitlyQualified);
                return;
            case CompoundTerm { Name: ".", Arity: 2 }:
            case AtomTerm { Name: "!" or "[]" }:
                return;
            case AtomTerm atom:
                _nonterminalCall(new PredicateIndicator(atom.Name, 0), module, explicitlyQualified);
                return;
            case CompoundTerm goal when goal.Arity == 2 && goal.Name is "," or ";" or "|" or "->" or "*->":
                WalkGrammarBody(goal.Arguments[0], module, explicitlyQualified);
                WalkGrammarBody(goal.Arguments[1], module, explicitlyQualified);
                return;
            case CompoundTerm { Name: "\\+", Arity: 1 } negation:
                WalkGrammarBody(negation.Arguments[0], module, explicitlyQualified);
                return;
            case CompoundTerm { Name: "call", Arity: 1 } call:
                WalkClosure(call.Arguments[0], 2, module, explicitlyQualified);
                return;
            case CompoundTerm { Name: "phrase", Arity: 1 } phrase:
                WalkNonterminal(phrase.Arguments[0], module, explicitlyQualified);
                return;
            case CompoundTerm nonterminal:
                _nonterminalCall(new PredicateIndicator(nonterminal.Name, nonterminal.Arity), module, explicitlyQualified);
                return;
            default:
                Incomplete = true;
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

    private void WalkMetaArguments(CompoundTerm goal, string module, bool explicitlyQualified)
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
                WalkGoal(goal.Arguments[index], module, explicitlyQualified);
            }
            else if (meta[index] > 0)
            {
                WalkClosure(goal.Arguments[index], meta[index], module, explicitlyQualified);
            }
        }
    }

    private void WalkClosure(SyntaxTerm term, int extraArguments, string module, bool explicitlyQualified)
    {
        switch (term)
        {
            case VariableTerm:
                Incomplete = true;
                return;
            case AtomTerm atom:
                _ordinaryCall(new PredicateIndicator(atom.Name, extraArguments), module, explicitlyQualified);
                return;
            case CompoundTerm { Name: ":", Arity: 2 } qualified when qualified.Arguments[0] is AtomTerm target:
                WalkClosure(qualified.Arguments[1], extraArguments, target.Name, explicitlyQualified: true);
                return;
            case CompoundTerm compound:
                _ordinaryCall(
                    new PredicateIndicator(compound.Name, compound.Arity + extraArguments),
                    module,
                    explicitlyQualified
                );
                return;
            default:
                Incomplete = true;
                return;
        }
    }

    private void WalkNonterminal(SyntaxTerm term, string module, bool explicitlyQualified)
    {
        switch (term)
        {
            case VariableTerm:
                Incomplete = true;
                return;
            case AtomTerm atom:
                _nonterminalCall(new PredicateIndicator(atom.Name, 0), module, explicitlyQualified);
                return;
            case CompoundTerm { Name: ":", Arity: 2 } qualified when qualified.Arguments[0] is AtomTerm target:
                WalkNonterminal(qualified.Arguments[1], target.Name, explicitlyQualified: true);
                return;
            case CompoundTerm compound:
                _nonterminalCall(new PredicateIndicator(compound.Name, compound.Arity), module, explicitlyQualified);
                return;
            default:
                Incomplete = true;
                return;
        }
    }
}
