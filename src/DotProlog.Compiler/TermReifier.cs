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
    internal static SyntaxTerm ToSyntax(Machine machine, Cell term)
    {
        Cell cell = machine.Dereference(term);

        switch (cell.Tag)
        {
            case CellTag.Reference:
                return new VariableTerm(string.Create(CultureInfo.InvariantCulture, $"_G{cell.Index}"), SourceSpan.None);

            case CellTag.Atom:
                return new AtomTerm(machine.Symbols.AtomName(cell.Index), SourceSpan.None);

            case CellTag.Integer:
                return new IntegerTerm(cell.Integer, SourceSpan.None);

            case CellTag.Float:
                return new FloatTerm(machine.Symbols.GetFloat(cell.Index), SourceSpan.None);

            case CellTag.Structure:
            {
                Functor functor = machine.Symbols.GetFunctor(machine.HeapAt(cell.Index).Index);
                var arguments = new SyntaxTerm[functor.Arity];
                for (int i = 0; i < functor.Arity; i++)
                {
                    arguments[i] = ToSyntax(machine, machine.HeapAt(cell.Index + 1 + i));
                }

                return new CompoundTerm(machine.Symbols.AtomName(functor.NameAtom), arguments, SourceSpan.None);
            }

            default:
                throw new PrologException($"type_error(callable, {cell})");
        }
    }

    /// <summary>Builds a heap term from a <see cref="SyntaxTerm"/>, sharing a cell per variable name.</summary>
    internal static Cell ToHeap(Machine machine, SyntaxTerm term, Dictionary<string, Cell> variables)
    {
        switch (term)
        {
            case VariableTerm variable:
            {
                // '_' never shares, so each occurrence gets its own cell.
                if (variable.IsAnonymous)
                {
                    return machine.CreateVariable();
                }

                if (!variables.TryGetValue(variable.Name, out Cell cell))
                {
                    cell = machine.CreateVariable();
                    variables[variable.Name] = cell;
                }

                return cell;
            }

            case AtomTerm atom:
                return Cell.Atom(machine.Symbols.InternAtom(atom.Name));

            case IntegerTerm integer when Cell.FitsInteger(integer.Value):
                return Cell.Integer60(integer.Value);

            case IntegerTerm integer:
                throw new PrologException($"representation_error(max_integer) for {integer.Value}");

            case FloatTerm number:
                return Cell.Float(machine.Symbols.InternFloat(number.Value));

            case CompoundTerm compound:
            {
                var arguments = new Cell[compound.Arity];
                for (int i = 0; i < compound.Arity; i++)
                {
                    arguments[i] = ToHeap(machine, compound.Arguments[i], variables);
                }

                return machine.CreateStructure(machine.Symbols.InternFunctor(compound.Name, compound.Arity), arguments);
            }

            default:
                throw new PrologException($"type_error(callable, {term.GetType().Name})");
        }
    }
}
