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
        bool indexFirstArgument = false,
        IReadOnlyDictionary<string, string>? libraryFiles = null
    )
    {
        using var engine = new PrologEngine(languageMode, flagOverrides)
        {
            Output = TextWriter.Null,
            Error = TextWriter.Null,
            Input = TextReader.Null,
        };

        if (libraryFiles is not null)
            engine.LibraryFiles = libraryFiles;

        // Backends preserve indexes; reference benchmark paths can request clause chains.
        engine.Program.EmitFirstArgumentIndexing = indexFirstArgument;

        foreach ((var name, var arity) in hostBuiltins)
        {
            engine.Program.Builtins.Register(name, arity, static _ => false);
        }

        var sourceMap = new CompilationSourceMap();
        engine.SourceMap = sourceMap;
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

        return CompiledProgramModel.Create(engine.Program, codeStart, initialization, preparation, sourceMap);
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
