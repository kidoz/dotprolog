using System.Runtime.Loader;
using BenchmarkDotNet.Attributes;
using DotProlog.CodeGen.IL;
using DotProlog.Compiler;
using DotProlog.Syntax;

namespace DotProlog.Benchmarks;

/// <summary>Separates lowering, direct IL emission, and installation; all output stays in memory.</summary>
[MemoryDiagnoser]
public class CompilerPipelineBenchmarks
{
    private string _source = string.Empty;
    private (string Name, string Text)[] _sources = [];
    private CompiledProgramModel _model = null!;
    private AssemblyLoadContext _context = null!;
    private Func<PrologEngine, int[]> _install = null!;
    private Func<PrologEngine, int[]> _installTable = null!;
    private PrologEngine _installationEngine = null!;

    [Params(1, 20, 100)]
    public int Repetitions { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _source = CompilerBenchmarkSource.Create(Repetitions);
        _sources = [("pipeline.pl", _source)];
        _model = CompilerBenchmarkSource.Lower(_sources);
        // Load once outside timing. Each measured installation gets a fresh engine because
        // program installation appends metadata and cannot be repeated on a fixed engine.
        _context = new AssemblyLoadContext(null, isCollectible: true);
        _install = LoadInstaller("PipelineFallback", linearVariableFallback: true);
        _installTable = LoadInstaller("PipelineTable", linearVariableFallback: false);
        // Exercise both generated installers and the shared host before single-invocation
        // measurements. Disable tiering in controlled runs if tier transitions still distort timing.
        for (var iteration = 0; iteration < 64; iteration++)
        {
            _install(CreateEngine());
            _installTable(CreateEngine());
        }
    }

    [Benchmark]
    public int Parse() => TermReader.ReadProgram(_source).Clauses.Count;

    [Benchmark]
    public int Lower() => CompilerBenchmarkSource.Lower(_sources).Instructions.Count;

    [Benchmark]
    public long EmitIl()
    {
        using var output = new MemoryStream();
        IlAssemblyEmitter.WriteAssembly(_model, "PipelineBenchmark", output);
        return output.Length;
    }

    [Benchmark]
    public long CompileToIl()
    {
        using var output = new MemoryStream();
        CompilerBenchmarkSource.Check(IlAssemblyEmitter.Emit(_sources, "PipelineBenchmark", output));
        return output.Length;
    }

    [Benchmark]
    public long EmitTableIl()
    {
        using var output = new MemoryStream();
        IlAssemblyEmitter.WriteAssembly(_model, "PipelineBenchmark", output, linearVariableFallback: false);
        return output.Length;
    }

    [Benchmark]
    public long CompileToTableIl()
    {
        using var output = new MemoryStream();
        CompilerBenchmarkSource.Check(
            IlAssemblyEmitter.Emit(_sources, "PipelineBenchmark", output, fuseBlocks: true, linearVariableFallback: false)
        );
        return output.Length;
    }

    [Benchmark]
    public PrologEngine CreateEngine() => new() { Output = TextWriter.Null };

    [Benchmark]
    public int[] CreateEngineAndInstallIl() => _install(CreateEngine());

    [Benchmark]
    public int[] CreateEngineAndInstallTableIl() => _installTable(CreateEngine());

    [IterationSetup(Targets = [nameof(InstallIl), nameof(InstallTableIl)])]
    public void SetupInstallation() => _installationEngine = CreateEngine();

    [Benchmark]
    public int[] InstallIl() => _install(_installationEngine);

    [Benchmark]
    public int[] InstallTableIl() => _installTable(_installationEngine);

    [GlobalCleanup]
    public void Cleanup() => _context.Unload();

    private Func<PrologEngine, int[]> LoadInstaller(string name, bool linearVariableFallback)
    {
        using var output = new MemoryStream();
        IlAssemblyEmitter.WriteAssembly(_model, name, output, linearVariableFallback: linearVariableFallback);
        output.Position = 0;
        return _context
            .LoadFromStream(output)
            .GetType(IlAssemblyEmitter.ProgramTypeName)!
            .GetMethod("Install")!
            .CreateDelegate<Func<PrologEngine, int[]>>();
    }
}
