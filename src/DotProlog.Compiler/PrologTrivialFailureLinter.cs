using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Checks ground calls against supplied clause heads without executing clause bodies.</summary>
internal static class PrologTrivialFailureLinter
{
    internal static void CheckCall(
        SyntaxTerm site,
        PredicateIndicator predicate,
        string targetModule,
        PrologLintProgramModel model,
        string fileName,
        List<Diagnostic> diagnostics
    )
    {
        IReadOnlyList<SyntaxTerm> arguments = site switch
        {
            AtomTerm => [],
            CompoundTerm compound => compound.Arguments,
            _ => [],
        };
        if (site is not (AtomTerm or CompoundTerm) || arguments.Any(argument => !IsSupportedGround(argument)))
        {
            return;
        }

        if (!model.TryStaticClauses(targetModule, predicate, out IReadOnlyList<PrologLintProgramModel.ClauseContext> clauses))
        {
            return;
        }

        foreach (PrologLintProgramModel.ClauseContext clause in clauses)
        {
            if (HeadMatches(arguments, LintSyntax.ClauseHead(clause.Clause)) != HeadMatch.NoMatch)
            {
                return;
            }
        }

        diagnostics.Add(
            new Diagnostic(
                LintDiagnosticIds.TrivialCallFailure,
                DiagnosticSeverity.Warning,
                $"No supplied clause head of {targetModule}:{LintSyntax.Display(predicate)} matches this ground call.",
                site.Span,
                fileName
            )
        );
    }

    private static HeadMatch HeadMatches(IReadOnlyList<SyntaxTerm> arguments, SyntaxTerm head)
    {
        if (ContainsReaderDependentText(head))
        {
            return HeadMatch.Unknown;
        }

        if (head is AtomTerm && arguments.Count == 0)
        {
            return HeadMatch.Match;
        }

        if (head is not CompoundTerm compound || compound.Arity != arguments.Count)
        {
            return HeadMatch.NoMatch;
        }

        Dictionary<string, SyntaxTerm> bindings = new(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!TermMatches(arguments[index], compound.Arguments[index], bindings))
            {
                return HeadMatch.NoMatch;
            }
        }

        return HeadMatch.Match;
    }

    private static bool TermMatches(SyntaxTerm argument, SyntaxTerm pattern, Dictionary<string, SyntaxTerm> bindings)
    {
        if (pattern is VariableTerm variable)
        {
            if (variable.IsAnonymous)
            {
                return true;
            }

            if (bindings.TryGetValue(variable.Name, out SyntaxTerm? bound))
            {
                return LintSyntax.CanonicalForm(argument) == LintSyntax.CanonicalForm(bound);
            }

            bindings.Add(variable.Name, argument);
            return true;
        }

        if (pattern is CompoundTerm compound)
        {
            if (argument is not CompoundTerm value || compound.Name != value.Name || compound.Arity != value.Arity)
            {
                return false;
            }

            for (var index = 0; index < compound.Arity; index++)
            {
                if (!TermMatches(value.Arguments[index], compound.Arguments[index], bindings))
                {
                    return false;
                }
            }

            return true;
        }

        return LintSyntax.CanonicalForm(argument) == LintSyntax.CanonicalForm(pattern);
    }

    private static bool IsSupportedGround(SyntaxTerm term) =>
        term switch
        {
            VariableTerm or StringTerm => false,
            CompoundTerm compound => compound.Arguments.All(IsSupportedGround),
            _ => true,
        };

    private static bool ContainsReaderDependentText(SyntaxTerm term)
    {
        if (term is StringTerm)
        {
            return true;
        }

        return term is CompoundTerm compound && compound.Arguments.Any(ContainsReaderDependentText);
    }

    private enum HeadMatch
    {
        Match,
        NoMatch,
        Unknown,
    }
}
