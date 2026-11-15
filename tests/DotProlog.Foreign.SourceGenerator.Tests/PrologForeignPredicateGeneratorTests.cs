using System.Reflection;
using DotProlog.Compiler;
using DotProlog.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotProlog.Foreign.SourceGenerator.Tests;

public sealed class PrologForeignPredicateGeneratorTests
{
    [Fact]
    public void EmitsStaticRegistryThatRuntimeBytecodeCanCall()
    {
        const string source = """
            using DotProlog.Runtime;

            internal static class CustomerPredicates
            {
                [PrologPredicate(Module = "customer", Name = "write_customer", Arity = 1)]
                internal static bool WriteCustomer(ref PrologContext context, Cell customer)
                {
                    context.Machine.Output.WriteLine(context.Format(customer));
                    return true;
                }
            }
            """;

        (var compilation, _, var generated) = Run(source);
        Assert.Contains("registry.Register(\"customer:write_customer\", 1", generated, StringComparison.Ordinal);
        Assert.Contains(
            "CustomerPredicates.WriteCustomer(ref context, machine.Argument(0))",
            generated,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("GetTypes", generated, StringComparison.Ordinal);

        var assembly = Emit(compilation);
        using var engine = new PrologEngine();
        var output = new StringWriter();
        engine.Output = output;
        InvokeRegistry(assembly, engine.Program.Builtins);
        engine.ConsultOrThrow(":- module(customer, []).", "customer.pl");

        Assert.True(engine.RunGoal("customer:write_customer(customer(42))", out _) == RunResult.Success);
        Assert.Equal("customer(42)" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void EmptyInputStillEmitsTheHostRegistryContract()
    {
        (_, _, var generated) = Run("internal static class Empty { }");

        Assert.Contains("internal static class ForeignPredicateRegistry", generated, StringComparison.Ordinal);
        Assert.Contains("internal static void Register", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("private static bool Wrong(ref PrologContext context) => true;", "public or internal static")]
    [InlineData("internal static int Wrong(ref PrologContext context) => 0;", "static bool")]
    [InlineData("internal static bool Wrong(PrologContext context) => true;", "ref DotProlog.Runtime.PrologContext")]
    [InlineData("internal static bool Wrong(ref PrologContext context, int value) => true;", "DotProlog.Runtime.Cell parameters")]
    public void InvalidSignatureProducesPositionedDiagnosticInsteadOfBrokenCSharp(string method, string message)
    {
        var source = $$"""
            using DotProlog.Runtime;
            internal static class Predicates
            {
                [PrologPredicate(Name = "wrong", Arity = 0)]
                {{method}}
            }
            """;

        (var compilation, var result, var generated) = Run(source);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("DPL4001", diagnostic.Id);
        Assert.Contains(message, diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("Foreign.cs", diagnostic.Location.GetLineSpan().Path);
        Assert.Equal(4, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        Assert.DoesNotContain("Predicates.Wrong", generated, StringComparison.Ordinal);
        Assert.True(
            compilation
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error)
        );
    }

    [Fact]
    public void InaccessibleContainingTypeIsASignatureDiagnostic()
    {
        const string source = """
            using DotProlog.Runtime;
            internal static class Outer
            {
                private static class Predicates
                {
                    [PrologPredicate(Name = "hidden", Arity = 0)]
                    internal static bool Hidden(ref PrologContext context) => true;
                }
            }
            """;

        (_, GeneratorDriverRunResult result, string generated) = Run(source);
        Assert.Equal("DPL4001", Assert.Single(result.Diagnostics).Id);
        Assert.DoesNotContain("Predicates.Hidden", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidDeclarationAndUnsupportedDeterminismHaveDistinctDiagnostics()
    {
        const string source = """
            using DotProlog.Runtime;
            internal static class Predicates
            {
                [PrologPredicate(Name = "", Arity = -1)]
                internal static bool Invalid(ref PrologContext context) => true;

                [PrologPredicate(Name = "many", Arity = 0, Determinism = "nondet")]
                internal static bool Many(ref PrologContext context) => true;
            }
            """;

        (_, var result, var generated) = Run(source);
        Assert.Equal(["DPL4002", "DPL4004"], result.Diagnostics.Select(diagnostic => diagnostic.Id));
        Assert.DoesNotContain("Predicates.Invalid", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Predicates.Many", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateRegistrationsAreLocatedAndNotEmitted()
    {
        const string source = """
            using DotProlog.Runtime;
            internal static class Predicates
            {
                [PrologPredicate(Name = "same", Arity = 0)]
                internal static bool First(ref PrologContext context) => true;

                [PrologPredicate(Name = "same", Arity = 0)]
                internal static bool Second(ref PrologContext context) => false;
            }
            """;

        (_, var result, var generated) = Run(source);
        Assert.Equal(2, result.Diagnostics.Length);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("DPL4003", diagnostic.Id));
        Assert.All(result.Diagnostics, diagnostic => Assert.True(diagnostic.Location.IsInSource));
        Assert.DoesNotContain("Predicates.First", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Predicates.Second", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationOrderIsDeterministicAndIndependentOfDeclarationOrder()
    {
        const string first = """
            using DotProlog.Runtime;
            internal static class Predicates
            {
                [PrologPredicate(Name = "zeta", Arity = 0)]
                internal static bool Z(ref PrologContext context) => true;
                [PrologPredicate(Name = "alpha", Arity = 0)]
                internal static bool A(ref PrologContext context) => true;
            }
            """;
        const string second = """
            using DotProlog.Runtime;
            internal static class Predicates
            {
                [PrologPredicate(Name = "alpha", Arity = 0)]
                internal static bool A(ref PrologContext context) => true;
                [PrologPredicate(Name = "zeta", Arity = 0)]
                internal static bool Z(ref PrologContext context) => true;
            }
            """;

        (_, _, var firstOutput) = Run(first);
        (_, _, var secondOutput) = Run(second);
        Assert.Equal(firstOutput, secondOutput);
    }

    [Fact]
    public void UnchangedDriverRunUsesCachedGeneratedOutput()
    {
        var compilation = Compilation("internal static class Empty { }");
        GeneratorDriver driver = Driver();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);

        var result = driver.GetRunResult();
        var reasons = result
            .Results[0]
            .TrackedSteps.SelectMany(step => step.Value)
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)
            .ToArray();
        Assert.Contains(IncrementalStepRunReason.Cached, reasons);
        Assert.DoesNotContain(IncrementalStepRunReason.Modified, reasons);
    }

    private static (Compilation Compilation, GeneratorDriverRunResult Result, string Generated) Run(string source)
    {
        var compilation = Compilation(source);
        GeneratorDriver driver = Driver();
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var output,
            out _,
            TestContext.Current.CancellationToken
        );
        var result = driver.GetRunResult();
        var generated = Assert.Single(result.GeneratedTrees).ToString();
        return (output, result, generated);
    }

    private static CSharpGeneratorDriver Driver() =>
        CSharpGeneratorDriver.Create(
            [new PrologForeignPredicateGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            driverOptions: new GeneratorDriverOptions(trackIncrementalGeneratorSteps: true)
        );

    private static CSharpCompilation Compilation(string source)
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return CSharpCompilation.Create(
            $"ForeignGenerator_{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), path: "Foreign.cs")],
            [
                MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Private.CoreLib.dll")),
                MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(Machine).Assembly.Location),
            ],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
    }

    private static Assembly Emit(Compilation compilation)
    {
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return Assembly.Load(image.ToArray());
    }

    private static void InvokeRegistry(Assembly assembly, BuiltinRegistry registry)
    {
        var type = assembly.GetType("DotProlog.Generated.ForeignPredicateRegistry")!;
        var register = type.GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static)!;
        register.Invoke(null, [registry]);
    }
}
