using Prolog.Syntax;

namespace Prolog.CodeGen.CSharp.Tests;

/// <summary>Reading a <c>.dpli</c> contract, and rejecting the ways it can be wrong.</summary>
public sealed class ContractReaderTests
{
    private static ContractReadResult Read(string contract) => ContractReader.Read(contract, "Fallback", "test.dpli");

    private static ModuleContract ReadValid(string contract)
    {
        ContractReadResult result = Read(contract);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return result.Contract!;
    }

    private static string ErrorId(string contract)
    {
        ContractReadResult result = Read(contract);
        Assert.False(result.Success);
        return result.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error).Id;
    }

    [Fact]
    public void ReadsTheModuleAndItsExports()
    {
        ModuleContract contract = ReadValid(
            """
            :- clr_module('Pricing').
            :- clr_export(discount/3, det, [in(price, float), in(percent, integer), out(result, float)]).
            """
        );

        Assert.Equal("Pricing", contract.ClrTypeName);
        ContractExport export = Assert.Single(contract.Exports);
        Assert.Equal("discount", export.PredicateName);
        Assert.Equal(3, export.Arity);
        Assert.Equal(Determinism.Det, export.Determinism);
        Assert.Equal(2, export.Inputs.Count());
        Assert.Single(export.Outputs);
    }

    [Fact]
    public void FallsBackToTheSuppliedNamespace()
    {
        Assert.Equal("Fallback", ReadValid(":- clr_module('M').").Namespace);
    }

    [Fact]
    public void ADeclaredNamespaceWins()
    {
        Assert.Equal("Contoso.Rules", ReadValid(":- clr_module('M').\n:- clr_namespace('Contoso.Rules').").Namespace);
    }

    [Theory]
    [InlineData("atom", "string")]
    [InlineData("integer", "long")]
    [InlineData("float", "double")]
    [InlineData("term", "global::Prolog.Runtime.PrologValue")]
    public void MapsEachScalarType(string prologType, string clrType)
    {
        ModuleContract contract = ReadValid($":- clr_module('M').\n:- clr_export(p/1, det, [out(v, {prologType})]).");

        Assert.Equal(clrType, contract.Exports[0].Arguments[0].Type.ClrTypeName);
    }

    [Fact]
    public void MapsNestedListTypes()
    {
        ModuleContract contract = ReadValid(":- clr_module('M').\n:- clr_export(p/1, det, [out(v, list(list(atom)))]).");

        Assert.Equal(
            "global::System.Collections.Generic.IReadOnlyList<global::System.Collections.Generic.IReadOnlyList<string>>",
            contract.Exports[0].Arguments[0].Type.ClrTypeName
        );
    }

    [Theory]
    [InlineData("det", Determinism.Det)]
    [InlineData("semidet", Determinism.Semidet)]
    [InlineData("multi", Determinism.Multi)]
    [InlineData("nondet", Determinism.Nondet)]
    public void ReadsEachDeterminism(string name, Determinism expected)
    {
        ModuleContract contract = ReadValid($":- clr_module('M').\n:- clr_export(p/1, {name}, [out(v, atom)]).");

        Assert.Equal(expected, contract.Exports[0].Determinism);
    }

    [Fact]
    public void AContractWithoutAModuleDeclarationIsRejected()
    {
        Assert.Equal(CodeGenDiagnosticIds.MissingModuleDeclaration, ErrorId(":- clr_export(p/1, det, [out(v, atom)])."));
    }

    [Fact]
    public void AnUnknownDirectiveIsRejected()
    {
        Assert.Equal(CodeGenDiagnosticIds.UnknownDirective, ErrorId(":- clr_module('M').\n:- something_else(1)."));
    }

    [Fact]
    public void AnExportWithoutAPredicateIndicatorIsRejected()
    {
        Assert.Equal(
            CodeGenDiagnosticIds.InvalidPredicateIndicator,
            ErrorId(":- clr_module('M').\n:- clr_export(p, det, [out(v, atom)]).")
        );
    }

    [Fact]
    public void AnUnknownDeterminismIsRejected()
    {
        Assert.Equal(
            CodeGenDiagnosticIds.UnknownDeterminism,
            ErrorId(":- clr_module('M').\n:- clr_export(p/1, sometimes, [out(v, atom)]).")
        );
    }

    [Fact]
    public void AnUnknownTypeIsRejected()
    {
        Assert.Equal(
            CodeGenDiagnosticIds.UnknownType,
            ErrorId(":- clr_module('M').\n:- clr_export(p/1, det, [out(v, widget)]).")
        );
    }

    [Fact]
    public void AnArgumentThatIsNotInOrOutIsRejected()
    {
        Assert.Equal(
            CodeGenDiagnosticIds.InvalidArgument,
            ErrorId(":- clr_module('M').\n:- clr_export(p/1, det, [maybe(v, atom)]).")
        );
    }

    [Fact]
    public void ArgumentCountMustMatchTheArity()
    {
        // A mismatch here would produce a facade that calls the predicate wrongly at run time.
        Assert.Equal(
            CodeGenDiagnosticIds.ArityMismatch,
            ErrorId(":- clr_module('M').\n:- clr_export(p/3, det, [out(v, atom)]).")
        );
    }

    [Fact]
    public void ADeterministicExportWithNoOutputsIsRejected()
    {
        // Its C# method would have no result at all; semidet is what was meant.
        Assert.Equal(
            CodeGenDiagnosticIds.DeterministicExportNeedsOutput,
            ErrorId(":- clr_module('M').\n:- clr_export(p/1, det, [in(v, atom)]).")
        );
    }

    [Fact]
    public void ClrExportFourNamesTheGeneratedMethod()
    {
        ModuleContract contract = ReadValid(
            ":- clr_module('M').\n:- clr_export(nrev/2, det, [in(l, list(atom)), out(r, list(atom))], 'NaiveReverse')."
        );

        Assert.Equal("NaiveReverse", contract.Exports[0].ClrName);
    }

    [Fact]
    public void AGeneratedNameThatIsNotAnAtomIsRejected()
    {
        Assert.Equal(
            CodeGenDiagnosticIds.InvalidClrName,
            ErrorId(":- clr_module('M').\n:- clr_export(p/1, det, [out(v, atom)], 42).")
        );
    }

    [Fact]
    public void TwoModesWithDifferentInputsOverloadCleanly()
    {
        // append(+,+,-) and append(+,-,-) take different numbers of inputs, so C# overloading covers it.
        ModuleContract contract = ReadValid(
            """
            :- clr_module('M').
            :- clr_export(append/3, det, [in(a, list(atom)), in(b, list(atom)), out(c, list(atom))]).
            :- clr_export(append/3, nondet, [in(c, list(atom)), out(a, list(atom)), out(b, list(atom))]).
            """
        );

        Assert.Equal(2, contract.Exports.Count);
    }

    [Fact]
    public void TwoModesWithTheSameInputsAreRejectedUnlessOneIsRenamed()
    {
        // Both take two lists, so both would generate the same C# method.
        const string Clashing = """
            :- clr_module('M').
            :- clr_export(append/3, nondet, [in(a, list(atom)), out(b, list(atom)), in(c, list(atom))]).
            :- clr_export(append/3, nondet, [out(a, list(atom)), in(b, list(atom)), in(c, list(atom))]).
            """;

        Assert.Equal(CodeGenDiagnosticIds.DuplicateMethodSignature, ErrorId(Clashing));

        // Naming one of them resolves it, which is what clr_export/4 is for.
        ModuleContract resolved = ReadValid(
            """
            :- clr_module('M').
            :- clr_export(append/3, nondet, [in(a, list(atom)), out(b, list(atom)), in(c, list(atom))], 'AppendLeft').
            :- clr_export(append/3, nondet, [out(a, list(atom)), in(b, list(atom)), in(c, list(atom))]).
            """
        );

        Assert.Equal("AppendLeft", resolved.Exports[0].ClrName);
    }

    [Fact]
    public void ReaderDiagnosticsSurfaceFromTheContractItself()
    {
        ContractReadResult result = Read(":- clr_module('M')\n:- clr_export(p/1, det, [out(v, atom)]).");

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Id == DiagnosticIds.MissingEndToken);
    }
}
