using System.Runtime.Loader;
using BenchmarkDotNet.Attributes;
using DotProlog.CodeGen.CSharp;
using DotProlog.CodeGen.IL;
using DotProlog.Compiler;
using DotProlog.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotProlog.Benchmarks;

/// <summary>Large-table hits, misses and complete scans across all three execution backends.</summary>
/// <remarks>Roslyn and assembly loading occur only in benchmark setup, outside production paths.</remarks>
[MemoryDiagnoser]
public class LargeClauseBenchmarks : IDisposable
{
    private AssemblyLoadContext _context = null!;
    private PrologEngine _bytecode = null!;
    private PrologEngine _csharp = null!;
    private PrologEngine _il = null!;
    private int _bytecodeGoal;
    private int _csharpGoal;
    private int _ilGoal;

    [Params(20, 4096)]
    public int Clauses { get; set; }

    [Params("Hit", "Miss", "Scan")]
    public string Workload { get; set; } = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        var source = string.Join('\n', Enumerable.Range(0, Clauses).Select(i => $"item({i})."));
        var goal = Workload switch
        {
            "Hit" => $"item({Clauses - 1})",
            "Miss" => $"\\+ item({Clauses})",
            _ => "(item(_), fail ; true)",
        };
        _context = new AssemblyLoadContext(null, isCollectible: true);
        _bytecode = new PrologEngine { Output = TextWriter.Null };
        _bytecode.ConsultOrThrow(source, "large-table.pl");
        _csharp = InstallCSharp(source);
        _il = new PrologEngine { Output = TextWriter.Null };
        using var image = new MemoryStream();
        CompilerBenchmarkSource.Check(IlAssemblyEmitter.Emit([("large-table.pl", source)], "LargeIl", image));
        image.Position = 0;
        _context
            .LoadFromStream(image)
            .GetType(IlAssemblyEmitter.ProgramTypeName)!
            .GetMethod("Install")!
            .CreateDelegate<Func<PrologEngine, int[]>>()(_il);
        _bytecodeGoal = CompileGoal(_bytecode, goal);
        _csharpGoal = CompileGoal(_csharp, goal);
        _ilGoal = CompileGoal(_il, goal);
        if (Bytecode() != RunResult.Success || GeneratedCSharp() != RunResult.Success || DirectIl() != RunResult.Success)
            throw new InvalidOperationException("Every backend must complete the workload.");
    }

    private PrologEngine InstallCSharp(string source)
    {
        var contract = ContractReader.Read(
            ":- clr_module('Large'). :- clr_export(item/1, semidet, [in(value, integer)]).",
            "Bench.Generated"
        );
        var generated = FacadeGenerator.Generate(contract.Contract!, source, "large-table.pl");
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string[] references =
        [
            "System.Private.CoreLib",
            "System.Runtime",
            "System.Linq",
            "System.Collections",
            "System.Runtime.Numerics",
        ];
        var metadata = references
            .Select(name => MetadataReference.CreateFromFile(Path.Combine(runtime, name + ".dll")))
            .Concat([
                MetadataReference.CreateFromFile(typeof(Machine).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(PrologEngine).Assembly.Location),
            ]);
        var compilation = CSharpCompilation.Create(
            "LargeCSharp",
            [CSharpSyntaxTree.ParseText(generated)],
            metadata,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        if (!result.Success)
            throw new InvalidOperationException(string.Join('\n', result.Diagnostics));
        image.Position = 0;
        var type = _context.LoadFromStream(image).GetType("Bench.Generated.LargeModule")!;
        var engine = new PrologEngine { Output = TextWriter.Null };
        type.GetMethod("Create", [typeof(PrologEngine)])!.Invoke(null, [engine]);
        return engine;
    }

    private static int CompileGoal(PrologEngine engine, string goal)
    {
        var address = engine.CompileGoal(goal, out var diagnostics);
        CompilerBenchmarkSource.Check(diagnostics);
        return address;
    }

    [Benchmark(Baseline = true)]
    public RunResult Bytecode() => _bytecode.Machine.Run(_bytecodeGoal);

    [Benchmark]
    public RunResult GeneratedCSharp() => _csharp.Machine.Run(_csharpGoal);

    [Benchmark]
    public RunResult DirectIl() => _il.Machine.Run(_ilGoal);

    [GlobalCleanup]
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;
        _bytecode?.Dispose();
        _csharp?.Dispose();
        _il?.Dispose();
        _context?.Unload();
    }
}
