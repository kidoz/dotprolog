using System.Globalization;
using System.Text;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>Shared, allocation-bounded helpers for source-local lint rules.</summary>
internal static class LintSyntax
{
    internal static IEnumerable<VariableTerm> Variables(SyntaxTerm term)
    {
        Stack<SyntaxTerm> pending = new();
        pending.Push(term);

        while (pending.TryPop(out SyntaxTerm? current))
        {
            switch (current)
            {
                case VariableTerm variable:
                    yield return variable;
                    break;
                case CompoundTerm compound:
                    for (var index = compound.Arguments.Count - 1; index >= 0; index--)
                    {
                        pending.Push(compound.Arguments[index]);
                    }

                    break;
            }
        }
    }

    internal static IEnumerable<SyntaxTerm> SequenceItems(SyntaxTerm term)
    {
        switch (term)
        {
            case CompoundTerm { Name: ",", Arity: 2 } sequence:
                foreach (SyntaxTerm item in SequenceItems(sequence.Arguments[0]))
                {
                    yield return item;
                }

                foreach (SyntaxTerm item in SequenceItems(sequence.Arguments[1]))
                {
                    yield return item;
                }

                break;
            case CompoundTerm { Name: ".", Arity: 2 } cons:
                yield return cons.Arguments[0];
                foreach (SyntaxTerm item in SequenceItems(cons.Arguments[1]))
                {
                    yield return item;
                }

                break;
            case AtomTerm { Name: "[]" }:
                break;
            default:
                yield return term;
                break;
        }
    }

    internal static bool TryProperList(SyntaxTerm term, out List<SyntaxTerm> elements)
    {
        elements = [];
        SyntaxTerm current = term;
        while (current is CompoundTerm { Name: ".", Arity: 2 } cons)
        {
            elements.Add(cons.Arguments[0]);
            current = cons.Arguments[1];
        }

        return current is AtomTerm { Name: "[]" };
    }

    internal static bool TryIndicator(SyntaxTerm term, out PredicateIndicator indicator, out bool nonterminal)
    {
        indicator = default;
        nonterminal = false;
        if (
            term is not CompoundTerm { Arity: 2 } slash
            || slash.Name is not ("/" or "//")
            || slash.Arguments[0] is not AtomTerm name
            || slash.Arguments[1] is not IntegerTerm { Value: >= 0 } arity
        )
        {
            return false;
        }

        nonterminal = slash.Name == "//";
        indicator = new PredicateIndicator(name.Name, checked((int)arity.Value + (nonterminal ? 2 : 0)));
        return true;
    }

    internal static SyntaxTerm ClauseHead(SyntaxTerm clause) =>
        clause switch
        {
            CompoundTerm { Name: ":-", Arity: 2 } rule => rule.Arguments[0],
            CompoundTerm { Name: "-->", Arity: 2 } grammar
                when grammar.Arguments[0] is CompoundTerm { Name: ",", Arity: 2 } semicontext => semicontext.Arguments[0],
            CompoundTerm { Name: "-->", Arity: 2 } grammar => grammar.Arguments[0],
            _ => clause,
        };

    internal static bool TryPredicate(SyntaxTerm head, bool grammar, out PredicateIndicator indicator)
    {
        indicator = default;
        switch (head)
        {
            case AtomTerm atom:
                indicator = new PredicateIndicator(atom.Name, grammar ? 2 : 0);
                return true;
            case CompoundTerm { Name: not (":" or ":-" or "-->") } compound:
                indicator = new PredicateIndicator(compound.Name, compound.Arity + (grammar ? 2 : 0));
                return true;
            default:
                return false;
        }
    }

    internal static string Display(PredicateIndicator indicator, bool nonterminal = false) =>
        $"{indicator.Name}{(nonterminal ? "//" : "/")}{(nonterminal ? indicator.Arity - 2 : indicator.Arity)}";

    internal static string CanonicalForm(SyntaxTerm term)
    {
        Dictionary<string, int> variables = new(StringComparer.Ordinal);
        var next = 0;
        StringBuilder text = new();
        AppendCanonical(term, variables, ref next, text);
        return text.ToString();
    }

    private static void AppendCanonical(SyntaxTerm term, Dictionary<string, int> variables, ref int next, StringBuilder text)
    {
        switch (term)
        {
            case VariableTerm variable:
                if (variable.IsAnonymous)
                {
                    text.Append('?').Append(next++);
                }
                else
                {
                    if (!variables.TryGetValue(variable.Name, out var id))
                    {
                        id = next++;
                        variables.Add(variable.Name, id);
                    }

                    text.Append('V').Append(id);
                }

                break;
            case AtomTerm atom:
                AppendQuoted(text, "a", atom.Name);
                break;
            case IntegerTerm integer:
                text.Append('i').Append(integer.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case BigIntegerTerm integer:
                text.Append('b').Append(integer.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case FloatTerm number:
                text.Append('f').Append(number.Value.ToString("R", CultureInfo.InvariantCulture));
                break;
            case RationalTerm rational:
                text.Append('r')
                    .Append(rational.Numerator.ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(rational.Denominator.ToString(CultureInfo.InvariantCulture));
                break;
            case StringTerm value:
                AppendQuoted(text, "dq", value.Value);
                break;
            case StringValueTerm value:
                AppendQuoted(text, "s", value.Value);
                break;
            case CompoundTerm compound:
                AppendQuoted(text, "c", compound.Name);
                text.Append('/').Append(compound.Arity).Append('(');
                foreach (SyntaxTerm argument in compound.Arguments)
                {
                    AppendCanonical(argument, variables, ref next, text);
                    text.Append(';');
                }

                text.Append(')');
                break;
        }
    }

    private static void AppendQuoted(StringBuilder target, string kind, string value)
    {
        target.Append(kind).Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
    }
}
