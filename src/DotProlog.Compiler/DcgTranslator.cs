using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>
/// Translates a definite clause grammar rule, <c>Head --&gt; Body</c>, into the ordinary clause the
/// compiler already knows how to lower.
/// </summary>
/// <remarks>
/// <para>
/// The translation threads a difference list through the body. Every non-terminal gains two
/// arguments — the list before it and the list after it — so <c>greeting --&gt; [hello], name</c>
/// becomes <c>greeting(S0, S) :- S0 = [hello|S1], name(S1, S)</c>. Nothing about the engine changes;
/// a grammar is ordinary Prolog once this has run.
/// </para>
/// <para>
/// An element that consumes nothing — <c>{Goal}</c>, <c>!</c>, the empty list — emits an explicit
/// <c>S0 = S</c> rather than being threaded through the same variable. That costs one unification
/// and keeps the translation a local rewrite of each element, which is what makes it readable.
/// </para>
/// </remarks>
internal sealed class DcgTranslator
{
    /// <summary>
    /// A name no lexer can produce, so a generated variable can never collide with one the grammar
    /// wrote. Variables are keyed by name when a clause is compiled, and a collision would silently
    /// merge two of them.
    /// </summary>
    private const string VariablePrefix = "_S$";

    private readonly List<Diagnostic> _diagnostics;
    private readonly string? _fileName;
    private readonly bool _strictIso;
    private int _next;

    private DcgTranslator(List<Diagnostic> diagnostics, string? fileName, PrologLanguageMode languageMode)
    {
        _diagnostics = diagnostics;
        _fileName = fileName;
        _strictIso = languageMode == PrologLanguageMode.StrictIso;
    }

    /// <summary>
    /// Translates a grammar rule into a head and body. Returns <see langword="false"/> and reports a
    /// diagnostic when the rule cannot be translated.
    /// </summary>
    internal static bool TryTranslate(
        CompoundTerm rule,
        List<Diagnostic> diagnostics,
        string? fileName,
        PrologLanguageMode languageMode,
        out SyntaxTerm head,
        out SyntaxTerm body
    )
    {
        var translator = new DcgTranslator(diagnostics, fileName, languageMode);
        return translator.Translate(rule, out head, out body);
    }

    private bool Translate(CompoundTerm rule, out SyntaxTerm head, out SyntaxTerm body)
    {
        head = rule;
        body = rule;

        SyntaxTerm left = rule.Arguments[0];
        SyntaxTerm pushback = new AtomTerm(TermReader.EmptyListAtom, rule.Span);
        bool hasPushback = false;

        // Head, PushBack --> Body puts PushBack back onto the input after the body has run.
        if (left is CompoundTerm { Name: ",", Arity: 2 } withPushback)
        {
            left = withPushback.Arguments[0];
            pushback = withPushback.Arguments[1];
            hasPushback = true;
        }

        if (left is not (AtomTerm or CompoundTerm))
        {
            Report(left, "A grammar rule head must be a non-terminal.");
            return false;
        }

        VariableTerm start = NewVariable(rule.Span);
        VariableTerm end = NewVariable(rule.Span);

        head = AddArguments(left, start, end);

        if (!hasPushback)
        {
            return TryTranslateBody(rule.Arguments[1], start, end, out body);
        }

        // With a pushback list the body stops at an intermediate point and the pushback is matched
        // from the rule's own end, so that what it names is left in front of whatever comes next.
        VariableTerm middle = NewVariable(rule.Span);
        if (!TryTranslateBody(rule.Arguments[1], start, middle, out SyntaxTerm inner))
        {
            return false;
        }

        if (!TryTranslateSemicontext(pushback, end, middle, out SyntaxTerm pushed))
        {
            return false;
        }

        body = Conjunction(inner, pushed, rule.Span);
        return true;
    }

    private bool TryTranslateSemicontext(SyntaxTerm semicontext, SyntaxTerm start, SyntaxTerm end, out SyntaxTerm goal)
    {
        if (semicontext is not AtomTerm { Name: "[]" } && semicontext is not CompoundTerm { Name: ".", Arity: 2 })
        {
            goal = semicontext;
            Report(semicontext, "A grammar rule semicontext must be a terminal sequence.");
            return false;
        }

        return TryTranslateTerminals(semicontext, start, end, out goal);
    }

    private bool TryTranslateBody(SyntaxTerm element, SyntaxTerm start, SyntaxTerm end, out SyntaxTerm goal)
    {
        goal = element;

        switch (element)
        {
            // A variable non-terminal is only known at run time, so it goes through phrase/3.
            case VariableTerm:
                goal = new CompoundTerm("phrase", [element, start, end], element.Span);
                return true;

            case StringTerm text:
                return TryTranslateBody(TermNormalizer.Normalize(text), start, end, out goal);

            case AtomTerm { Name: "[]" }:
                goal = Unify(start, end, element.Span);
                return true;

            case AtomTerm { Name: "!" }:
                goal = Conjunction(element, Unify(start, end, element.Span), element.Span);
                return true;

            case CompoundTerm { Name: "{}", Arity: 1 } braced:
                goal = Conjunction(braced.Arguments[0], Unify(start, end, element.Span), element.Span);
                return true;

            case CompoundTerm { Name: ".", Arity: 2 }:
                return TryTranslateTerminals(element, start, end, out goal);

            case CompoundTerm { Name: ",", Arity: 2 } conjunction:
            {
                VariableTerm middle = NewVariable(element.Span);
                if (
                    !TryTranslateBody(conjunction.Arguments[0], start, middle, out SyntaxTerm first)
                    || !TryTranslateBody(conjunction.Arguments[1], middle, end, out SyntaxTerm second)
                )
                {
                    return false;
                }

                goal = Conjunction(first, second, element.Span);
                return true;
            }

            case CompoundTerm
            {
                Name: ";",
                Arity: 2,
                Arguments: [CompoundTerm { Name: "*->", Arity: 2 } softIfThen, SyntaxTerm alternative],
            } when !_strictIso:
                return TryTranslateSoftIf(
                    softIfThen.Arguments[0],
                    softIfThen.Arguments[1],
                    alternative,
                    start,
                    end,
                    element.Span,
                    out goal
                );

            // Both branches of a disjunction consume the same input and leave the same remainder.
            case CompoundTerm { Name: ";", Arity: 2 }
            or CompoundTerm { Name: "|", Arity: 2 }:
            {
                var alternatives = (CompoundTerm)element;
                if (
                    !TryTranslateBody(alternatives.Arguments[0], start, end, out SyntaxTerm left)
                    || !TryTranslateBody(alternatives.Arguments[1], start, end, out SyntaxTerm right)
                )
                {
                    return false;
                }

                goal = new CompoundTerm(";", [left, right], element.Span);
                return true;
            }

            case CompoundTerm { Name: "->", Arity: 2 } ifThen:
            {
                VariableTerm middle = NewVariable(element.Span);
                if (
                    !TryTranslateBody(ifThen.Arguments[0], start, middle, out SyntaxTerm condition)
                    || !TryTranslateBody(ifThen.Arguments[1], middle, end, out SyntaxTerm then)
                )
                {
                    return false;
                }

                goal = new CompoundTerm("->", [condition, then], element.Span);
                return true;
            }

            case CompoundTerm { Name: "*->", Arity: 2 } softIfThen when !_strictIso:
                return TryTranslateSoftIf(
                    softIfThen.Arguments[0],
                    softIfThen.Arguments[1],
                    alternative: null,
                    start,
                    end,
                    element.Span,
                    out goal
                );

            // Negation consumes nothing, whether or not the goal inside it would have.
            case CompoundTerm { Name: "\\+", Arity: 1 } negation:
            {
                if (!TryTranslateBody(negation.Arguments[0], start, NewVariable(element.Span), out SyntaxTerm inner))
                {
                    return false;
                }

                goal = Conjunction(new CompoundTerm("\\+", [inner], element.Span), Unify(start, end, element.Span), element.Span);

                return true;
            }

            case CompoundTerm { Name: "call", Arity: 1 } call:
                goal = new CompoundTerm("call", [.. call.Arguments, start, end], element.Span);
                return true;

            case AtomTerm
            or CompoundTerm:
                goal = AddArguments(element, start, end);
                return true;

            default:
                Report(element, "A grammar rule body may not contain a number.");
                return false;
        }
    }

    private bool TryTranslateSoftIf(
        SyntaxTerm conditionBody,
        SyntaxTerm thenBody,
        SyntaxTerm? alternative,
        SyntaxTerm start,
        SyntaxTerm end,
        SourceSpan span,
        out SyntaxTerm goal
    )
    {
        VariableTerm middle = NewVariable(span);
        if (
            !TryTranslateBody(conditionBody, start, middle, out SyntaxTerm condition)
            || !TryTranslateBody(thenBody, middle, end, out SyntaxTerm then)
        )
        {
            goal = conditionBody;
            return false;
        }

        var soft = new CompoundTerm("*->", [condition, then], span);
        if (alternative is null)
        {
            goal = soft;
            return true;
        }

        if (!TryTranslateBody(alternative, start, end, out SyntaxTerm otherwise))
        {
            goal = alternative;
            return false;
        }

        goal = new CompoundTerm(";", [soft, otherwise], span);
        return true;
    }

    /// <summary>
    /// Matches a list of terminals by unifying the input with them followed by the remainder, so
    /// <c>[a, b]</c> becomes <c>S0 = [a, b|S]</c> and consumes both in one step.
    /// </summary>
    private bool TryTranslateTerminals(SyntaxTerm list, SyntaxTerm start, SyntaxTerm end, out SyntaxTerm goal)
    {
        List<SyntaxTerm> terminals = [];
        SyntaxTerm current = list;

        while (current is CompoundTerm { Name: ".", Arity: 2 } cons)
        {
            terminals.Add(cons.Arguments[0]);
            current = cons.Arguments[1];
        }

        if (current is not AtomTerm { Name: "[]" })
        {
            goal = list;
            Report(list, "A list of terminals in a grammar rule must be a proper list.");
            return false;
        }

        SyntaxTerm matched = end;
        for (int i = terminals.Count - 1; i >= 0; i--)
        {
            matched = new CompoundTerm(TermReader.ListFunctor, [terminals[i], matched], list.Span);
        }

        goal = Unify(start, matched, list.Span);
        return true;
    }

    private static SyntaxTerm AddArguments(SyntaxTerm nonTerminal, SyntaxTerm start, SyntaxTerm end) =>
        nonTerminal switch
        {
            AtomTerm atom => new CompoundTerm(atom.Name, [start, end], atom.Span),
            CompoundTerm compound => new CompoundTerm(compound.Name, [.. compound.Arguments, start, end], compound.Span),
            _ => nonTerminal,
        };

    private static CompoundTerm Conjunction(SyntaxTerm first, SyntaxTerm second, SourceSpan span) =>
        new CompoundTerm(",", [first, second], span);

    private static CompoundTerm Unify(SyntaxTerm left, SyntaxTerm right, SourceSpan span) =>
        new CompoundTerm("=", [left, right], span);

    private VariableTerm NewVariable(SourceSpan span) => new($"{VariablePrefix}{_next++}", span);

    private void Report(SyntaxTerm term, string message) =>
        _diagnostics.Add(
            new Diagnostic(CompilerDiagnosticIds.InvalidGrammarRule, DiagnosticSeverity.Error, message, term.Span, _fileName)
        );
}
