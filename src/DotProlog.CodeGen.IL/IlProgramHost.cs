using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Text;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.CodeGen.IL;

/// <summary>Installs statically referenced IL blocks and their portable metadata into an engine.</summary>
/// <remarks>Generated-code contract. The image contains registration data, never predicate bytecode or source.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class IlProgramHost
{
    /// <summary>Runs the initialization goals of a generated console application.</summary>
    public static int Run(string image, CompiledPredicateBlock[] blocks)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(blocks);
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(image), writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            ValidateHeader(reader);
            var mode = (PrologLanguageMode)reader.ReadInt32();
            var quotes = (DoubleQuotesMode)reader.ReadInt32();
            var engine = new PrologEngine(mode, new PrologFlagOverrides { DoubleQuotes = quotes });
            var initializers = Install(engine, image, blocks);
            var result = RunResult.Success;
            foreach (var target in initializers)
            {
                result = engine.Machine.Run(target);
                if (result is RunResult.Halted or RunResult.Failure)
                {
                    break;
                }
            }
            return result switch
            {
                RunResult.Halted => engine.Machine.ExitCode,
                RunResult.Failure => 1,
                _ => 0,
            };
        }
        catch (PrologException error)
        {
            Console.Error.WriteLine($"error: {error.Message}");
            return 70;
        }
        finally
        {
            Console.Out.Flush();
        }
    }

    /// <summary>Registers a generated program and returns its deferred initialization targets.</summary>
    public static int[] Install(PrologEngine engine, string image, CompiledPredicateBlock[] blocks)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(blocks);
        using var stream = new MemoryStream(Convert.FromBase64String(image), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        var version = ValidateHeader(reader);
        var runtime = engine.Program;
        if (
            (PrologLanguageMode)reader.ReadInt32() != runtime.LanguageMode
            || (DoubleQuotesMode)reader.ReadInt32() != runtime.InitialDoubleQuotes
        )
        {
            throw new PrologException("Generated program language mode or initial double_quotes does not match the engine.");
        }
        if (reader.ReadInt32() != blocks.Length)
        {
            throw new InvalidDataException("Generated program block count does not match its installation image.");
        }
        var symbols = runtime.Symbols;
        var functors = new int[ReadCount(reader)];
        for (var i = 0; i < functors.Length; i++)
        {
            functors[i] = symbols.InternFunctor(reader.ReadString(), reader.ReadInt32());
        }
        var builtins = new int[ReadCount(reader)];
        for (var i = 0; i < builtins.Length; i++)
        {
            var functor = functors[reader.ReadInt32()];
            if (!runtime.Builtins.TryGetId(functor, out builtins[i]))
            {
                throw new PrologException($"Required builtin {symbols.DescribeFunctor(functor)} is not registered.");
            }
        }
        var constants = new Cell[ReadCount(reader)];
        for (var i = 0; i < constants.Length; i++)
        {
            var tag = (CellTag)reader.ReadByte();
            var text = reader.ReadString();
            var integer = reader.ReadInt64();
            var real = reader.ReadDouble();
            constants[i] = tag switch
            {
                CellTag.Atom => Cell.Atom(symbols.InternAtom(text)),
                CellTag.String => Cell.String(symbols.InternAtom(text)),
                CellTag.Integer => Cell.Integer60(integer),
                CellTag.Float => Cell.Float(symbols.InternFloat(real)),
                CellTag.BigInteger => Cell.Big(symbols.InternBig(BigInteger.Parse(text, CultureInfo.InvariantCulture))),
                CellTag.Rational => Rational(symbols, text),
                _ => throw new InvalidDataException("Unsupported generated constant tag."),
            };
        }
        var indexes = new int[version >= 2 ? ReadCount(reader) : 0];
        var compiled = new CompiledProgram(functors, builtins, constants, blocks.Length, indexes);
        ReadModules(reader, runtime, compiled);
        for (var i = 0; i < blocks.Length; i++)
        {
            compiled.SetTarget(i, runtime.RegisterCompiledBlock(blocks[i], compiled));
        }
        for (var i = 0; i < indexes.Length; i++)
        {
            var keys = ReadTerm(reader, compiled);
            var targets = new int[keys.Length];
            for (var j = 0; j < targets.Length; j++)
            {
                targets[j] = compiled.Target(reader.ReadInt32());
            }
            indexes[i] = runtime.AddStaticIndex(targets, keys);
        }
        var preparationCount = ReadCount(reader);
        var enteringQuotes = runtime.Flags.DoubleQuotes;
        try
        {
            for (var i = 0; i < preparationCount; i++)
            {
                ReadPredicates(reader, runtime, compiled);
                if (engine.Machine.Run(compiled.Target(reader.ReadInt32())) is RunResult.Failure)
                {
                    engine.Output.Write("Warning: directive failed.\n");
                }
            }
        }
        finally
        {
            runtime.Flags.SetDoubleQuotes(enteringQuotes);
        }
        ReadPredicates(reader, runtime, compiled);
        var dynamicCount = ReadCount(reader);
        for (var i = 0; i < dynamicCount; i++)
        {
            var functor = compiled.Functor(reader.ReadInt32());
            runtime.DeclareCompiledDynamic(functor);
            var clauseCount = ReadCount(reader);
            for (var j = 0; j < clauseCount; j++)
            {
                var entry = compiled.Target(reader.ReadInt32());
                var term = ReadTerm(reader, compiled);
                runtime.AddCompiledDynamicClause(functor, entry, term, reader.ReadInt32());
            }
            var aliasCount = ReadCount(reader);
            for (var j = 0; j < aliasCount; j++)
            {
                runtime.AliasPredicate(compiled.Functor(reader.ReadInt32()), functor);
            }
        }
        var initializers = new int[ReadCount(reader)];
        for (var i = 0; i < initializers.Length; i++)
        {
            initializers[i] = compiled.Target(reader.ReadInt32());
        }
        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException("Unexpected data after the installation image.");
        }
        return initializers;
    }

    private static Cell Rational(SymbolTable symbols, string text)
    {
        var separator = text.IndexOf('r', StringComparison.Ordinal);
        return Cell.Rational(
            symbols.InternRational(
                BigInteger.Parse(text.AsSpan(0, separator), CultureInfo.InvariantCulture),
                BigInteger.Parse(text.AsSpan(separator + 1), CultureInfo.InvariantCulture)
            )
        );
    }

    private static int ValidateHeader(BinaryReader reader)
    {
        var magic = reader.ReadInt32();
        var version = reader.ReadInt32();
        if (magic != InstallationImage.Magic || version is not (1 or InstallationImage.Version))
        {
            throw new InvalidDataException("Unsupported DotProlog IL installation image.");
        }
        return version;
    }

    private static int ReadCount(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > reader.BaseStream.Length - reader.BaseStream.Position)
        {
            throw new InvalidDataException("Invalid installation image length.");
        }
        return count;
    }

    private static void ReadPredicates(BinaryReader reader, BytecodeProgram runtime, CompiledProgram compiled)
    {
        var count = ReadCount(reader);
        for (var i = 0; i < count; i++)
        {
            runtime.DefinePredicate(compiled.Functor(reader.ReadInt32()), compiled.Target(reader.ReadInt32()));
        }
    }

    private static Cell[] ReadTerm(BinaryReader reader, CompiledProgram compiled)
    {
        var cells = new Cell[ReadCount(reader)];
        for (var i = 0; i < cells.Length; i++)
        {
            var tag = (CellTag)reader.ReadByte();
            var value = reader.ReadInt32();
            cells[i] = tag switch
            {
                CellTag.Reference => Cell.Reference(value),
                CellTag.Structure => Cell.Structure(value),
                CellTag.Functor => Cell.Functor(compiled.Functor(value)),
                CellTag.Atom or CellTag.String or CellTag.Integer or CellTag.BigInteger or CellTag.Rational or CellTag.Float =>
                    compiled.Constant(value),
                _ => throw new InvalidDataException("Unsupported term tag."),
            };
        }
        return cells;
    }

    private static void ReadModules(BinaryReader reader, BytecodeProgram runtime, CompiledProgram compiled)
    {
        var count = ReadCount(reader);
        for (var i = 0; i < count; i++)
        {
            var module = runtime.Modules.Declare(reader.ReadString());
            module.InterfacePrepared = reader.ReadBoolean();
            module.Operators.Clear();
            var operatorCount = ReadCount(reader);
            for (var j = 0; j < operatorCount; j++)
            {
                module.Operators.Define(reader.ReadInt32(), (OperatorType)reader.ReadInt32(), reader.ReadString());
            }
            module.CharacterConversions.Clear();
            var conversionCount = ReadCount(reader);
            for (var j = 0; j < conversionCount; j++)
            {
                module.CharacterConversions.Set(reader.ReadInt32(), reader.ReadInt32());
            }
            module.Flags.SetCharConversion(reader.ReadBoolean());
            module.Flags.SetDebug(reader.ReadBoolean());
            module.Flags.SetDoubleQuotes((DoubleQuotesMode)reader.ReadInt32());
            module.Flags.SetUnknown((UnknownProcedureAction)reader.ReadInt32());
            var predicateCount = ReadCount(reader);
            for (var j = 0; j < predicateCount; j++)
            {
                var predicate = module.Predicate(new ModulePredicateIndicator(reader.ReadString(), reader.ReadInt32()));
                predicate.Defined = reader.ReadBoolean();
                predicate.Exported = reader.ReadBoolean();
                predicate.Dynamic = reader.ReadBoolean();
                predicate.Multifile = reader.ReadBoolean();
                if (reader.ReadBoolean())
                {
                    predicate.MetapredicateTemplate = reader.ReadString();
                }
                var clauseCount = ReadCount(reader);
                for (var k = 0; k < clauseCount; k++)
                {
                    var cells = ReadTerm(reader, compiled);
                    predicate.AddStaticClause(cells, reader.ReadInt32());
                }
            }
            var importCount = ReadCount(reader);
            for (var j = 0; j < importCount; j++)
            {
                _ = module.TryImport(
                    new ModulePredicateIndicator(reader.ReadString(), reader.ReadInt32()),
                    reader.ReadString(),
                    out _
                );
            }
        }
    }
}
