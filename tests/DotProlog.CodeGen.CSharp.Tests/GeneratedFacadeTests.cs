using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace DotProlog.CodeGen.CSharp.Tests;

/// <summary>
/// Generates a facade, compiles it with Roslyn, loads it, and calls it.
/// </summary>
/// <remarks>
/// Asserting on generated text only proves the text. Compiling and running it is what proves the
/// generator emits C# that works — including that the marshalling expressions type-check.
/// </remarks>
public sealed class GeneratedFacadeTests
{
    private static readonly string[] SplitInput = ["a", "b"];

    private const string PrologSource = """
        discount(Price, Percent, Result) :- Result is Price - (Price * Percent / 100).

        colour(red).
        colour(green).
        colour(blue).

        in_stock(widget).
        in_stock(gadget).

        stock_level(widget, 7).

        split([], [], []).
        split([H|T], [H|L], R) :- split(T, L, R).
        split([H|T], L, [H|R]) :- split(T, L, R).
        """;

    private const string Contract = """
        :- clr_module('Pricing').
        :- clr_namespace('Generated.Pricing').

        :- clr_export(discount/3, det, [in(price, float), in(percent, integer), out(result, float)]).
        :- clr_export(colour/1, nondet, [out(colour, atom)]).
        :- clr_export(in_stock/1, semidet, [in(item, atom)]).
        :- clr_export(stock_level/2, semidet, [in(item, atom), out(level, integer)]).
        :- clr_export(split/3, nondet, [in(items, list(atom)), out(left, list(atom)), out(right, list(atom))]).
        """;

    private static ModuleContract ReadContract()
    {
        ContractReadResult result = ContractReader.Read(Contract, "Fallback", "pricing.dpli");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return result.Contract!;
    }

    private static object CreateModule(out Type moduleType)
    {
        string source = FacadeGenerator.Generate(ReadContract(), PrologSource, "pricing.pl");

        CSharpCompilation compilation = CSharpCompilation.Create(
            $"GeneratedFacade_{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            ReferenceAssemblies(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        using var image = new MemoryStream();
        EmitResult emitted = compilation.Emit(image);

        Assert.True(
            emitted.Success,
            "The generated facade did not compile:\n"
                + string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                + "\n\n"
                + source
        );

        Assembly assembly = Assembly.Load(image.ToArray());
        moduleType = assembly.GetType("Generated.Pricing.PricingModule")!;
        Assert.NotNull(moduleType);

        return moduleType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
    }

    private static MetadataReference[] ReferenceAssemblies()
    {
        string runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        return
        [
            MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Private.CoreLib.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Linq.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Collections.dll")),
            MetadataReference.CreateFromFile(typeof(Runtime.Machine).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Compiler.PrologEngine).Assembly.Location),
        ];
    }

    private static object? Call(object module, Type type, string method, params object?[] arguments) =>
        type.GetMethod(method)!.Invoke(module, arguments);

    [Fact]
    public void GeneratedFacadeCompilesAndLoads()
    {
        object module = CreateModule(out Type type);

        Assert.NotNull(module);
        Assert.Contains(type.GetInterfaces(), i => i.Name == "IPricingModule");
    }

    [Fact]
    public void DeterministicExportReturnsItsOutput()
    {
        object module = CreateModule(out Type type);

        Assert.Equal(90.0, Call(module, type, "Discount", 100.0, 10L));
    }

    [Fact]
    public void SemiDeterministicExportWithNoOutputsReturnsBool()
    {
        object module = CreateModule(out Type type);

        Assert.Equal(true, Call(module, type, "InStock", "widget"));
        Assert.Equal(false, Call(module, type, "InStock", "sprocket"));
    }

    [Fact]
    public void SemiDeterministicExportWithAnOutputBecomesATryMethod()
    {
        object module = CreateModule(out Type type);
        MethodInfo tryStockLevel = type.GetMethod("TryStockLevel")!;

        object?[] found = ["widget", null];
        Assert.Equal(true, tryStockLevel.Invoke(module, found));
        Assert.Equal(7L, found[1]);

        object?[] missing = ["gadget", null];
        Assert.Equal(false, tryStockLevel.Invoke(module, missing));
    }

    [Fact]
    public void NondeterministicExportStreamsEverySolution()
    {
        object module = CreateModule(out Type type);

        // Streaming methods take a CancellationToken, which reflection must supply explicitly.
        var colours = (IEnumerable<string>)Call(module, type, "Colour", CancellationToken.None)!;

        Assert.Equal(["red", "green", "blue"], colours);
    }

    [Fact]
    public void ListsCrossTheBoundaryInBothDirections()
    {
        object module = CreateModule(out Type type);

        var splits = (System.Collections.IEnumerable)Call(module, type, "Split", SplitInput, CancellationToken.None)!;
        List<string> shapes = [];

        foreach (object split in splits)
        {
            Type resultType = split.GetType();
            var left = (IReadOnlyList<string>)resultType.GetProperty("Left")!.GetValue(split)!;
            var right = (IReadOnlyList<string>)resultType.GetProperty("Right")!.GetValue(split)!;
            shapes.Add($"{string.Concat(left)}|{string.Concat(right)}");
        }

        Assert.Equal(["ab|", "a|b", "b|a", "|ab"], shapes);
    }

    [Fact]
    public void SeveralOutputsBecomeAGeneratedRecord()
    {
        string source = FacadeGenerator.Generate(ReadContract(), PrologSource, "pricing.pl");

        Assert.Contains("public readonly record struct SplitResult(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void APredicateNamedAfterACSharpKeywordStillGenerates()
    {
        // 'double' is a keyword: the parameter form needs an @ escape, the field form must not have
        // one, because '_@double2' is not a legal identifier.
        ContractReadResult contract = ContractReader.Read(
            ":- clr_module('Maths').\n:- clr_export(double/2, det, [in(double, integer), out(result, integer)]).",
            "Generated.Maths",
            "maths.dpli"
        );

        Assert.True(contract.Success, string.Join("; ", contract.Diagnostics));

        string source = FacadeGenerator.Generate(contract.Contract!, "double(X, Y) :- Y is X * 2.", "maths.pl");

        Assert.Contains("_double2;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_@", source, StringComparison.Ordinal);
        Assert.Contains("long @double", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PrologNamesBecomeIdiomaticCSharpNames()
    {
        string source = FacadeGenerator.Generate(ReadContract(), PrologSource, "pricing.pl");

        Assert.Contains("bool InStock(string item)", source, StringComparison.Ordinal);
        Assert.Contains("_host.Bind(\"in_stock\", 1)", source, StringComparison.Ordinal);
    }
}
