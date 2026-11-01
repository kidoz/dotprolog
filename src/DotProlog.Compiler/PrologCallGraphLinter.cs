using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Reports call-resolution findings over a non-executing whole-program lint model.</summary>
internal static class PrologCallGraphLinter
{
    internal static void Analyze(PrologLintProgramModel model, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        CheckDeclarations(model, diagnostics);
        foreach (PrologLintProgramModel.ClauseContext clause in model.Clauses)
        {
            if (clause.Clause is CompoundTerm { Name: ":-", Arity: 2 } rule)
            {
                AnalyzeGoal(rule.Arguments[1], clause.Module, model, clause.Source.FileName, diagnostics);
            }
            else if (clause.Clause is CompoundTerm { Name: "-->", Arity: 2 } grammar)
            {
                AnalyzeGrammarBody(grammar.Arguments[1], clause.Module, model, clause.Source.FileName, diagnostics);
            }
            else if (
                clause.Clause is CompoundTerm
                {
                    Name: ":-",
                    Arity: 1,
                    Arguments: [CompoundTerm { Name: "initialization" } directive]
                }
            )
            {
                AnalyzeGoal(directive.Arguments[0], clause.Module, model, clause.Source.FileName, diagnostics);
            }
        }
    }

    private static void CheckDeclarations(PrologLintProgramModel model, List<Diagnostic> diagnostics)
    {
        HashSet<(string Module, PredicateIndicator Indicator, string Kind)> reported = [];
        foreach (PrologLintProgramModel.DeclarationSite declaration in model.Declarations)
        {
            if (
                model.Modules.Defines(declaration.Module, declaration.Indicator)
                || !reported.Add((declaration.Module, declaration.Indicator, declaration.Kind))
            )
            {
                continue;
            }

            diagnostics.Add(
                Warning(
                    LintDiagnosticIds.MissingPredicateDefinition,
                    $"The {declaration.Kind} declaration for {LintSyntax.Display(declaration.Indicator, declaration.Nonterminal)} "
                        + "has no definition in the supplied program.",
                    declaration.Span,
                    declaration.FileName
                )
            );
        }
    }

    private static void AnalyzeGoal(
        SyntaxTerm term,
        string module,
        PrologLintProgramModel model,
        string fileName,
        List<Diagnostic> diagnostics
    )
    {
        switch (term)
        {
            case VariableTerm:
                return;
            case AtomTerm atom:
                if (atom.Name is not ("!" or "true" or "fail" or "false" or "[]"))
                {
                    CheckOrdinaryCall(atom, new PredicateIndicator(atom.Name, 0), module, model, fileName, diagnostics);
                }

                return;
            case CompoundTerm goal when goal.Name == ":" && goal.Arity == 2:
                if (goal.Arguments[0] is AtomTerm qualified)
                {
                    AnalyzeGoal(goal.Arguments[1], qualified.Name, model, fileName, diagnostics);
                }

                return;
            case CompoundTerm goal when goal.Arity == 2 && goal.Name is "," or ";" or "|" or "->" or "*->":
                AnalyzeGoal(goal.Arguments[0], module, model, fileName, diagnostics);
                AnalyzeGoal(goal.Arguments[1], module, model, fileName, diagnostics);
                return;
            case CompoundTerm goal when goal.Name == "\\+" && goal.Arity == 1:
                AnalyzeGoal(goal.Arguments[0], module, model, fileName, diagnostics);
                return;
            case CompoundTerm goal when goal.Name == "^" && goal.Arity == 2:
                AnalyzeGoal(goal.Arguments[1], module, model, fileName, diagnostics);
                return;
            case CompoundTerm goal:
                CheckOrdinaryCall(goal, new PredicateIndicator(goal.Name, goal.Arity), module, model, fileName, diagnostics);
                if (goal.Name == "phrase" && goal.Arity is 2 or 3)
                {
                    AnalyzeNonterminal(goal.Arguments[0], module, model, fileName, diagnostics);
                    return;
                }

                AnalyzeMetaArguments(goal, module, model, fileName, diagnostics);
                return;
        }
    }

    private static void AnalyzeMetaArguments(
        CompoundTerm goal,
        string module,
        PrologLintProgramModel model,
        string fileName,
        List<Diagnostic> diagnostics
    )
    {
        int[]? meta = model.MetaArgumentsOf(module, new PredicateIndicator(goal.Name, goal.Arity));
        if (meta is null)
        {
            return;
        }

        for (var index = 0; index < meta.Length; index++)
        {
            if (meta[index] == 0)
            {
                AnalyzeGoal(goal.Arguments[index], module, model, fileName, diagnostics);
            }
            else if (meta[index] > 0)
            {
                AnalyzeClosure(goal.Arguments[index], meta[index], module, model, fileName, diagnostics);
            }
        }
    }

    private static void AnalyzeClosure(
        SyntaxTerm term,
        int extraArguments,
        string module,
        PrologLintProgramModel model,
        string fileName,
        List<Diagnostic> diagnostics
    )
    {
        switch (term)
        {
            case VariableTerm:
                return;
            case AtomTerm atom:
                CheckOrdinaryCall(
                    atom,
                    new PredicateIndicator(atom.Name, extraArguments),
                    module,
                    model,
                    fileName,
                    diagnostics,
                    allowTrivialFailure: false
                );
                return;
            case CompoundTerm { Name: ":", Arity: 2 } qualified when qualified.Arguments[0] is AtomTerm target:
                AnalyzeClosure(qualified.Arguments[1], extraArguments, target.Name, model, fileName, diagnostics);
                return;
            case CompoundTerm compound:
                CheckOrdinaryCall(
                    compound,
                    new PredicateIndicator(compound.Name, compound.Arity + extraArguments),
                    module,
                    model,
                    fileName,
                    diagnostics,
                    allowTrivialFailure: false
                );
                return;
        }
    }

    private static void AnalyzeGrammarBody(
        SyntaxTerm term,
        string module,
        PrologLintProgramModel model,
        string fileName,
        List<Diagnostic> diagnostics
    )
    {
        switch (term)
        {
            case VariableTerm or StringTerm:
                return;
            case CompoundTerm { Name: "{}", Arity: 1 } ordinary:
                AnalyzeGoal(ordinary.Arguments[0], module, model, fileName, diagnostics);
                return;
            case CompoundTerm { Name: ".", Arity: 2 }:
            case AtomTerm { Name: "!" or "[]" }:
                return;
            case AtomTerm atom:
                CheckNonterminalCall(atom, module, model, fileName, diagnostics);
                return;
            case CompoundTerm goal when goal.Arity == 2 && goal.Name is "," or ";" or "|" or "->" or "*->":
                AnalyzeGrammarBody(goal.Arguments[0], module, model, fileName, diagnostics);
                AnalyzeGrammarBody(goal.Arguments[1], module, model, fileName, diagnostics);
                return;
            case CompoundTerm goal when goal.Name == "\\+" && goal.Arity == 1:
                AnalyzeGrammarBody(goal.Arguments[0], module, model, fileName, diagnostics);
                return;
            case CompoundTerm { Name: "call", Arity: 1 } call:
                AnalyzeClosure(call.Arguments[0], 2, module, model, fileName, diagnostics);
                return;
            case CompoundTerm { Name: "phrase", Arity: 1 } phrase:
                AnalyzeNonterminal(phrase.Arguments[0], module, model, fileName, diagnostics);
                return;
            case CompoundTerm nonterminal:
                CheckNonterminalCall(nonterminal, module, model, fileName, diagnostics);
                return;
        }
    }

    private static void AnalyzeNonterminal(
        SyntaxTerm term,
        string module,
        PrologLintProgramModel model,
        string fileName,
        List<Diagnostic> diagnostics
    )
    {
        switch (term)
        {
            case VariableTerm:
                return;
            case AtomTerm atom:
                CheckNonterminalCall(atom, module, model, fileName, diagnostics);
                return;
            case CompoundTerm { Name: ":", Arity: 2 } qualified when qualified.Arguments[0] is AtomTerm target:
                AnalyzeNonterminal(qualified.Arguments[1], target.Name, model, fileName, diagnostics);
                return;
            case CompoundTerm compound:
                CheckNonterminalCall(compound, module, model, fileName, diagnostics);
                return;
        }
    }

    private static void CheckOrdinaryCall(
        SyntaxTerm site,
        PredicateIndicator predicate,
        string module,
        PrologLintProgramModel model,
        string fileName,
        List<Diagnostic> diagnostics,
        bool allowTrivialFailure = true
    )
    {
        PrologLintProgramModel.LintResolution resolution = model.Resolve(module, predicate);
        if (!resolution.Known)
        {
            diagnostics.Add(
                Warning(
                    LintDiagnosticIds.UndefinedPredicate,
                    $"Predicate {module}:{LintSyntax.Display(predicate)} is not defined in the supplied program.",
                    site.Span,
                    fileName
                )
            );
            return;
        }

        if (resolution.Module is { } target)
        {
            if (model.IsNonterminal(target, predicate) && !model.IsOrdinary(target, predicate))
            {
                diagnostics.Add(
                    Warning(
                        LintDiagnosticIds.PredicateNonterminalMismatch,
                        $"Predicate {LintSyntax.Display(predicate)} is called as an ordinary predicate, but only a nonterminal with the corresponding arity is defined.",
                        site.Span,
                        fileName
                    )
                );
                return;
            }

            if (allowTrivialFailure)
            {
                PrologTrivialFailureLinter.CheckCall(site, predicate, target, model, fileName, diagnostics);
            }
        }
    }

    private static void CheckNonterminalCall(
        SyntaxTerm site,
        string module,
        PrologLintProgramModel model,
        string fileName,
        List<Diagnostic> diagnostics
    )
    {
        if (!TrySourceIndicator(site, out PredicateIndicator sourceIndicator))
        {
            return;
        }

        PredicateIndicator compiled = new(sourceIndicator.Name, sourceIndicator.Arity + 2);
        PrologLintProgramModel.LintResolution resolution = model.Resolve(module, compiled);
        if (resolution.Module is { } target && model.IsNonterminal(target, compiled))
        {
            return;
        }

        PrologLintProgramModel.LintResolution ordinary = model.Resolve(module, sourceIndicator);
        if (ordinary.Module is { } ordinaryModule && model.IsOrdinary(ordinaryModule, sourceIndicator))
        {
            diagnostics.Add(
                Warning(
                    LintDiagnosticIds.PredicateNonterminalMismatch,
                    $"Nonterminal {LintSyntax.Display(sourceIndicator, nonterminal: true)} is called here, but only an ordinary predicate with that source arity is defined.",
                    site.Span,
                    fileName
                )
            );
            return;
        }

        if (!resolution.Known && !ordinary.Known)
        {
            diagnostics.Add(
                Warning(
                    LintDiagnosticIds.UndefinedPredicate,
                    $"Nonterminal {module}:{sourceIndicator.Name}//{sourceIndicator.Arity} is not defined in the supplied program.",
                    site.Span,
                    fileName
                )
            );
        }
    }

    private static bool TrySourceIndicator(SyntaxTerm term, out PredicateIndicator indicator)
    {
        indicator = term switch
        {
            AtomTerm atom => new PredicateIndicator(atom.Name, 0),
            CompoundTerm compound => new PredicateIndicator(compound.Name, compound.Arity),
            _ => default,
        };
        return term is AtomTerm or CompoundTerm;
    }

    private static Diagnostic Warning(string id, string message, SourceSpan span, string? fileName) =>
        new(id, DiagnosticSeverity.Warning, message, span, fileName);
}
