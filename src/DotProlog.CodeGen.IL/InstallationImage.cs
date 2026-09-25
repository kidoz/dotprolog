using System.Text;
using DotProlog.Compiler;

namespace DotProlog.CodeGen.IL;

/// <summary>Portable installation data only; executable operations live in emitted IL methods.</summary>
internal static class InstallationImage
{
    internal const int Magic = 0x44504C49;
    internal const int Version = 1;

    internal static string Encode(CompiledProgramModel model)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write((int)model.LanguageMode);
        writer.Write((int)model.InitialDoubleQuotes);
        writer.Write(model.Instructions.Count);
        writer.Write(model.Functors.Count);
        foreach ((var name, var arity) in model.Functors)
        {
            writer.Write(name);
            writer.Write(arity);
        }
        WriteIntegers(writer, model.Builtins);
        writer.Write(model.Constants.Count);
        foreach (CompiledConstant constant in model.Constants)
        {
            writer.Write((byte)constant.Tag);
            writer.Write(constant.Text ?? string.Empty);
            writer.Write(constant.Integer);
            writer.Write(constant.Float);
        }
        writer.Write(model.Modules.Count);
        foreach (CompiledModule module in model.Modules)
        {
            writer.Write(module.Name);
            writer.Write(module.InterfacePrepared);
            writer.Write(module.Operators.Count);
            foreach (var op in module.Operators)
            {
                writer.Write(op.Priority);
                writer.Write((int)op.Type);
                writer.Write(op.Name);
            }
            writer.Write(module.CharacterConversions.Count);
            foreach ((var input, var output) in module.CharacterConversions)
            {
                writer.Write(input);
                writer.Write(output);
            }
            writer.Write(module.CharConversion);
            writer.Write(module.Debug);
            writer.Write((int)module.DoubleQuotes);
            writer.Write((int)module.Unknown);
            writer.Write(module.Predicates.Count);
            foreach (CompiledModulePredicate predicate in module.Predicates)
            {
                writer.Write(predicate.Name);
                writer.Write(predicate.Arity);
                writer.Write(predicate.Defined);
                writer.Write(predicate.Exported);
                writer.Write(predicate.Dynamic);
                writer.Write(predicate.Multifile);
                writer.Write(predicate.MetapredicateTemplate is not null);
                if (predicate.MetapredicateTemplate is not null)
                {
                    writer.Write(predicate.MetapredicateTemplate);
                }
                writer.Write(predicate.StaticClauses.Count);
                foreach (CompiledModuleClause clause in predicate.StaticClauses)
                {
                    WriteTerm(writer, clause.Term);
                    writer.Write(clause.Root);
                }
            }
            writer.Write(module.Imports.Count);
            foreach (CompiledModuleImport import in module.Imports)
            {
                writer.Write(import.Name);
                writer.Write(import.Arity);
                writer.Write(import.From);
            }
        }
        writer.Write(model.Preparation.Count);
        foreach (PreparationStep step in model.Preparation)
        {
            WritePredicates(writer, step.Predicates);
            writer.Write(step.Directive);
        }
        WritePredicates(writer, model.Predicates);
        writer.Write(model.DynamicPredicates.Count);
        foreach (CompiledDynamicPredicate predicate in model.DynamicPredicates)
        {
            writer.Write(predicate.Functor);
            writer.Write(predicate.Clauses.Count);
            foreach (CompiledDynamicClause clause in predicate.Clauses)
            {
                writer.Write(clause.Entry);
                WriteTerm(writer, clause.Term);
                writer.Write(clause.Root);
            }
            WriteIntegers(writer, predicate.Aliases);
        }
        WriteIntegers(writer, model.Initialization);
        writer.Flush();
        return Convert.ToBase64String(stream.ToArray());
    }

    private static void WriteIntegers(BinaryWriter writer, List<int> values)
    {
        writer.Write(values.Count);
        foreach (var value in values)
        {
            writer.Write(value);
        }
    }

    private static void WritePredicates(BinaryWriter writer, List<CompiledPredicate> predicates)
    {
        writer.Write(predicates.Count);
        foreach (CompiledPredicate predicate in predicates)
        {
            writer.Write(predicate.Functor);
            writer.Write(predicate.Entry);
        }
    }

    private static void WriteTerm(BinaryWriter writer, List<CompiledTermCell> cells)
    {
        writer.Write(cells.Count);
        foreach (CompiledTermCell cell in cells)
        {
            writer.Write((byte)cell.Tag);
            writer.Write(cell.Value);
        }
    }
}
