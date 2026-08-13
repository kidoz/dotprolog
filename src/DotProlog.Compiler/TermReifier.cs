using System.Globalization;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

/// <summary>
/// Converts between heap terms and reader terms, which is what lets <c>assertz/1</c> compile a term
/// built at run time and lets the loader store a clause the database can match against.
/// </summary>
internal static class TermReifier
{
    /// <summary>
    /// Rebuilds a heap term as a <see cref="SyntaxTerm"/> so the clause compiler can lower it.
    /// Variables are named after their heap address, which keeps repeated occurrences of the same
    /// variable sharing a name.
    /// </summary>
    internal static SyntaxTerm ToSyntax(Machine machine, Cell term) => ToSyntaxCore(machine, term, null);

    /// <summary>
    /// Rebuilds a heap term as a <see cref="SyntaxTerm"/> and records each distinct live variable by
    /// the generated name used in that syntax tree.
    /// </summary>
    internal static SyntaxTerm ToSyntax(Machine machine, Cell term, Dictionary<string, Cell> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        return ToSyntaxCore(machine, term, variables);
    }

    /// <summary>
    /// Rebuilds a meta-called control goal as syntax, keeping only the control skeleton
    /// structural. Every argument of a leaf goal — bound or unbound — becomes a shared variable
    /// carried through an argument register, so the compiled goal operates on the caller's live
    /// cells rather than rebuilt copies; <c>setarg/3</c> makes the difference observable.
    /// </summary>
    internal static SyntaxTerm ToControlSyntax(Machine machine, Cell term, Dictionary<string, Cell> variables)
    {
        Cell cell = machine.Dereference(term);

        if (cell.Tag != CellTag.Structure)
        {
            return ToSyntaxCore(machine, cell, variables);
        }

        Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(cell.Index).Index);
        var name = machine.Symbols.AtomName(functor.NameAtom);
        var control = (functor.Arity == 2 && name is "," or ";" or "->" or "*->") || (functor.Arity == 1 && name == "\\+");

        var arguments = new SyntaxTerm[functor.Arity];
        for (var i = 0; i < functor.Arity; i++)
        {
            Cell argument = machine.HeapAt(cell.Index + 1 + i);
            arguments[i] = control ? ToControlSyntax(machine, argument, variables) : LeafArgument(machine, argument, variables);
        }

        return new CompoundTerm(name, arguments, SourceSpan.None);
    }

    // A structure argument keeps the caller's cell the same way an unbound variable does: it is
    // named after its heap address and handed over through a register. Immediate cells have no
    // identity to lose and stay literal.
    private static SyntaxTerm LeafArgument(Machine machine, Cell term, Dictionary<string, Cell> variables)
    {
        Cell cell = machine.Dereference(term);

        if (cell.Tag != CellTag.Structure)
        {
            return ToSyntaxCore(machine, cell, variables);
        }

        var name = string.Create(CultureInfo.InvariantCulture, $"_G{cell.Index}");
        variables.TryAdd(name, cell);
        return new VariableTerm(name, SourceSpan.None);
    }

    private static SyntaxTerm ToSyntaxCore(Machine machine, Cell term, Dictionary<string, Cell>? variables)
    {
        Cell cell = machine.Dereference(term);

        switch (cell.Tag)
        {
            case CellTag.Reference:
            {
                var name = string.Create(CultureInfo.InvariantCulture, $"_G{cell.Index}");
                variables?.TryAdd(name, cell);
                return new VariableTerm(name, SourceSpan.None);
            }

            case CellTag.Atom:
                return new AtomTerm(machine.Symbols.AtomName(cell.Index), SourceSpan.None);

            case CellTag.Integer:
                return new IntegerTerm(cell.Integer, SourceSpan.None);

            case CellTag.BigInteger:
                return new BigIntegerTerm(machine.Symbols.GetBig(cell.Index), SourceSpan.None);

            case CellTag.Rational:
            {
                (System.Numerics.BigInteger numerator, System.Numerics.BigInteger denominator) = machine.Symbols.GetRational(
                    cell.Index
                );
                return new RationalTerm(numerator, denominator, SourceSpan.None);
            }

            case CellTag.Float:
                return new FloatTerm(machine.Symbols.GetFloat(cell.Index), SourceSpan.None);

            case CellTag.String:
                return new StringValueTerm(machine.Symbols.AtomName(cell.Index), SourceSpan.None);

            case CellTag.Structure:
            {
                Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(cell.Index).Index);
                var arguments = new SyntaxTerm[functor.Arity];
                for (var i = 0; i < functor.Arity; i++)
                {
                    arguments[i] = ToSyntaxCore(machine, machine.HeapAt(cell.Index + 1 + i), variables);
                }

                return new CompoundTerm(machine.Symbols.AtomName(functor.NameAtom), arguments, SourceSpan.None);
            }

            default:
                throw new PrologException($"type_error(callable, {cell})");
        }
    }

    /// <summary>
    /// Builds a heap term from a <see cref="SyntaxTerm"/>, sharing a cell per variable name and
    /// optionally recording every distinct variable in first-occurrence order.
    /// </summary>
    internal static Cell ToHeap(
        Machine machine,
        SyntaxTerm term,
        Dictionary<string, Cell> variables,
        List<Cell>? variableOrder = null
    )
    {
        switch (term)
        {
            case VariableTerm variable:
            {
                // '_' never shares, so each occurrence gets its own cell.
                if (variable.IsAnonymous)
                {
                    Cell anonymous = machine.CreateVariable();
                    variableOrder?.Add(anonymous);
                    return anonymous;
                }

                if (!variables.TryGetValue(variable.Name, out Cell cell))
                {
                    cell = machine.CreateVariable();
                    variables[variable.Name] = cell;
                    variableOrder?.Add(cell);
                }

                return cell;
            }

            case AtomTerm atom:
                return Cell.Atom(machine.Symbols.InternAtom(atom.Name));

            case IntegerTerm integer when Cell.FitsInteger(integer.Value):
                return Cell.Integer60(integer.Value);

            case IntegerTerm integer:
                return Cell.Big(machine.Symbols.InternBig(integer.Value));

            case BigIntegerTerm big:
                return Cell.Big(machine.Symbols.InternBig(big.Value));

            case RationalTerm rational:
                return ArithmeticEvaluator.ToCell(machine, PrologNumber.FromRational(rational.Numerator, rational.Denominator));

            case FloatTerm number:
                return Cell.Float(machine.Symbols.InternFloat(number.Value));

            case StringValueTerm text:
                return Cell.String(machine.Symbols.InternAtom(text.Value));

            case CompoundTerm compound:
            {
                if (compound.Arity >= Machine.ArgumentRegisterCount)
                {
                    throw PrologErrors.Representation(machine, "max_arity");
                }

                var arguments = new Cell[compound.Arity];
                for (var i = 0; i < compound.Arity; i++)
                {
                    arguments[i] = ToHeap(machine, compound.Arguments[i], variables, variableOrder);
                }

                return machine.CreateStructure(machine.Symbols.InternFunctor(compound.Name, compound.Arity), arguments);
            }

            default:
                throw new PrologException($"type_error(callable, {term.GetType().Name})");
        }
    }
}
