using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler;

internal static class CompiledProgramBuilder
{
    internal static CompiledProgramModel? Compile(
        IReadOnlyList<(string Name, string Text)> sources,
        IReadOnlyList<(string Name, int Arity)> hostBuiltins,
        PrologLanguageMode languageMode,
        PrologFlagOverrides flagOverrides,
        out IReadOnlyList<Diagnostic> diagnostics,
        bool indexFirstArgument = false
    )
    {
        var engine = new PrologEngine(languageMode, flagOverrides)
        {
            Output = TextWriter.Null,
            Error = TextWriter.Null,
            Input = TextReader.Null,
        };

        // Generated C# retains clause chains; direct IL can preserve the loader's indexes.
        engine.Program.EmitFirstArgumentIndexing = indexFirstArgument;

        foreach ((var name, var arity) in hostBuiltins)
        {
            engine.Program.Builtins.Register(name, arity, static _ => false);
        }

        var codeStart = engine.Program.CodeLength;
        List<Diagnostic> allDiagnostics = [];
        List<int> initialization = [];
        List<RawPreparationStep> preparation = [];

        foreach ((var name, var source) in sources)
        {
            LoadResult loaded = engine.CompileForGeneratedCode(
                source,
                name,
                address => preparation.Add(new RawPreparationStep(address, SnapshotPredicates(engine.Program, codeStart)))
            );
            allDiagnostics.AddRange(loaded.Diagnostics);
            initialization.AddRange(loaded.InitializationAddresses);
        }

        diagnostics = allDiagnostics;
        if (allDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return null;
        }

        return CompiledProgramModel.Create(engine.Program, codeStart, initialization, preparation);
    }

    private static List<(int Functor, int Entry)> SnapshotPredicates(BytecodeProgram program, int codeStart)
    {
        List<(int Functor, int Entry)> predicates = [];
        for (var functor = 0; functor < program.Symbols.FunctorCount; functor++)
        {
            var entry = program.EntryPointOf(functor);
            if (program.IsUserPredicate(functor) && !program.IsDynamic(functor) && entry >= codeStart)
            {
                predicates.Add((functor, entry));
            }
        }

        return predicates;
    }
}
