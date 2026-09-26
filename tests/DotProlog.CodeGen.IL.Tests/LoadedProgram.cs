using System.Runtime.Loader;
using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.CodeGen.IL.Tests;

internal sealed class LoadedProgram : IDisposable
{
    private readonly AssemblyLoadContext _context = new(null, isCollectible: true);
    private readonly Func<PrologEngine, int[]> _install;
    private readonly Func<int> _run;

    internal LoadedProgram(
        string source,
        PrologLanguageMode mode = PrologLanguageMode.Modern,
        PrologFlagOverrides? overrides = null,
        bool fuseBlocks = true,
        bool indexFirstArgument = true,
        bool linearVariableFallback = true
    )
    {
        using var stream = new MemoryStream();
        var diagnostics = IlAssemblyEmitter.Emit(
            [("test.pl", source)],
            "TestProgram",
            stream,
            fuseBlocks,
            mode,
            overrides,
            indexFirstArgument,
            linearVariableFallback
        );
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        stream.Position = 0;
        var assembly = _context.LoadFromStream(stream);
        var method = assembly.GetType(IlAssemblyEmitter.ProgramTypeName)!.GetMethod("Install")!;
        _install = method.CreateDelegate<Func<PrologEngine, int[]>>();
        _run = assembly.EntryPoint!.CreateDelegate<Func<int>>();
    }

    internal int[] Install(PrologEngine engine) => _install(engine);

    internal int Run() => _run();

    public void Dispose() => _context.Unload();
}
