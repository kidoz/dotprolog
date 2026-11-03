using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.CodeGen.CSharp;

internal static class ContractTermCompatibility
{
    internal static bool MatchesGroundInput(ContractType type, SyntaxTerm term)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(term);

        if (type.Kind == ContractTypeKind.Term)
        {
            return TryGroundValue(term, out _);
        }

        if (type.Kind == ContractTypeKind.List)
        {
            return MatchesListInput(type.Element!, term);
        }

        return type.Kind switch
        {
            ContractTypeKind.Atom => term is AtomTerm,
            ContractTypeKind.Integer => term is IntegerTerm,
            ContractTypeKind.Float => term is FloatTerm,
            _ => false,
        };
    }

    internal static bool TryGroundValue(SyntaxTerm term, out PrologValue value)
    {
        ArgumentNullException.ThrowIfNull(term);
        switch (term)
        {
            case AtomTerm atom:
                value = new PrologAtom(atom.Name);
                return true;
            case IntegerTerm integer:
                value = IntegerValue(PrologNumber.FromInteger(integer.Value));
                return true;
            case BigIntegerTerm integer:
                value = IntegerValue(PrologNumber.FromBig(integer.Value));
                return true;
            case FloatTerm real:
                value = new PrologFloat(real.Value);
                return true;
            case RationalTerm rational:
                PrologNumber number = PrologNumber.FromRational(rational.Numerator, rational.Denominator);
                value = number.IsRational ? new PrologRational(number.Numerator, number.Denominator) : IntegerValue(number);
                return true;
            case StringValueTerm text:
                value = new PrologString(text.Value);
                return true;
            case CompoundTerm compound:
                var arguments = new PrologValue[compound.Arity];
                for (var index = 0; index < arguments.Length; index++)
                {
                    if (!TryGroundValue(compound.Arguments[index], out PrologValue? argument))
                    {
                        value = null!;
                        return false;
                    }

                    arguments[index] = argument;
                }

                value = new PrologCompound(compound.Name, arguments);
                return true;
            default:
                value = null!;
                return false;
        }
    }

    internal static string ShapeOf(PrologValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value switch
        {
            PrologAtom => "atom",
            PrologInteger => "integer",
            PrologBigInteger => "big_integer",
            PrologFloat => "float",
            PrologRational => "rational",
            PrologString => "string",
            PrologVariable => "variable",
            PrologCompound compound when value.TryGetList(out _) => "list",
            PrologCompound compound => $"compound({compound.Name}/{compound.Arguments.Count})",
            _ => "term",
        };
    }

    private static bool MatchesListInput(ContractType element, SyntaxTerm term)
    {
        SyntaxTerm current = term;
        while (current is CompoundTerm { Name: ".", Arity: 2 } pair)
        {
            if (!MatchesGroundInput(element, pair.Arguments[0]))
            {
                return false;
            }

            current = pair.Arguments[1];
        }

        return current is AtomTerm { Name: "[]" };
    }

    private static PrologValue IntegerValue(PrologNumber number) =>
        number.IsBig ? new PrologBigInteger(number.Big) : new PrologInteger(number.Integer);
}
