using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.CodeGen.IL.Tests;

public sealed class IlAssemblyEmitterTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("p(a).", 0)]
    [InlineData(":- initialization(true).", 0)]
    [InlineData(":- initialization(fail). :- initialization(halt(42)).", 1)]
    [InlineData(":- initialization(halt(7)). :- initialization(fail).", 7)]
    [InlineData(":- initialization(throw(problem)).", 70)]
    public void EntryPointReturnsInitializationStatus(string source, int expected)
    {
        using var compiled = new LoadedProgram(source);
        Assert.Equal(expected, compiled.Run());
    }

    [Theory]
    [InlineData("p(a). p(b). p(c).", "findall(X,p(X),[a,b,c])")]
    [InlineData("p(f(X), X).", "p(f(answer),answer)")]
    [InlineData("p(X,X).", "\\+ p(a,b), p(a,a)")]
    [InlineData("p(f(a,b)). p(f(c,d)).", "p(f(c,d))")]
    [InlineData("p([H|T],H,T).", "p([a,b],a,[b])")]
    [InlineData("p(X) :- (X=a;X=b).", "findall(X,p(X),[a,b])")]
    [InlineData("p(X) :- (X=a;X=b), !. p(c).", "findall(X,p(X),[a])")]
    [InlineData("p(X) :- (fail -> X=a; X=b).", "p(b)")]
    [InlineData("p(X) :- ((X=a;X=b) *-> true; X=c).", "findall(X,p(X),[a,b])")]
    [InlineData("p(X) :- catch(throw(ball(value)),ball(X),true).", "p(value)")]
    [InlineData("p(X) :- call(=(X,yes)).", "p(yes)")]
    [InlineData("p(X) :- X is 2+3*4.", "p(14)")]
    [InlineData("p(1.25). p(115292150460684697600). p(1r3).", "p(1.25), p(115292150460684697600), p(1r3)")]
    [InlineData("p(0). p(N) :- N>0, M is N-1, p(M).", "p(10000)")]
    [InlineData(
        "parent(a,b). parent(b,c). ancestor(X,Y):-parent(X,Y). ancestor(X,Y):-parent(X,Z),ancestor(Z,Y).",
        "findall(Y,ancestor(a,Y),[b,c])"
    )]
    [InlineData("word --> [a], [b].", "phrase(word,[a,b])")]
    public void EmittedPredicatesPreserveLanguageSemantics(string source, string goal)
    {
        using var compiled = new LoadedProgram(source);
        var engine = new PrologEngine();
        compiled.Install(engine);
        Assert.True(engine.Query(goal).Prove());
    }

    [Fact]
    public void InstallationRegistersNativeBlocksWithoutAppendingPredicateBytecode()
    {
        using var compiled = new LoadedProgram("p(a). p(b).");
        var engine = new PrologEngine();
        var originalCodeLength = engine.Program.CodeLength;
        compiled.Install(engine);
        Assert.Equal(originalCodeLength, engine.Program.CodeLength);
        Assert.True(engine.Program.EntryPointOf(engine.Program.Symbols.InternFunctor("p", 1)) < -1);
        Assert.True(engine.Query("findall(X,p(X),[a,b])").Prove());
    }

    [Fact]
    public void CompiledAndConsultedPredicatesCallEachOther()
    {
        using var compiled = new LoadedProgram("compiled(a). compiled(b). bridge(X) :- consulted(X).");
        var engine = new PrologEngine();
        compiled.Install(engine);
        Assert.True(engine.ConsultText("consulted(X) :- compiled(X).").Success);
        Assert.True(engine.Query("findall(X,bridge(X),[a,b])").Prove());
    }

    [Fact]
    public void DynamicClausesRetainLogicalUpdateView()
    {
        using var compiled = new LoadedProgram(
            """
            :- dynamic(p/1).
            p(first).
            collect(Xs) :- findall(X, (p(X), (X=first -> assertz(p(later)); true)), Xs).
            """
        );
        var engine = new PrologEngine();
        compiled.Install(engine);
        Assert.True(engine.Query("collect([first]), p(later), retract(p(first)), \\+ p(first)").Prove());
    }

    [Fact]
    public void PreparationRunsAtItsSourcePositionAndInitializationIsDeferred()
    {
        using var output = new StringWriter();
        using var compiled = new LoadedProgram(
            """
            value(before).
            :- value(X), write(X).
            value(after).
            :- initialization(writeln(initialized)).
            """
        );
        var engine = new PrologEngine { Output = output };
        var initializers = compiled.Install(engine);
        Assert.Equal("before", output.ToString());
        Assert.True(engine.Query("findall(X,value(X),[before,after])").Prove());
        Assert.Equal(RunResult.Success, engine.Machine.Run(Assert.Single(initializers)));
        Assert.Equal("beforeinitialized\n", output.ToString());
    }

    [Fact]
    public void ModuleMetadataAndPredicateAliasesSurviveInstallation()
    {
        using var compiled = new LoadedProgram(
            """
            :- module(colours, [colour/1]).
            colour(red).
            colour(blue).
            """
        );
        var engine = new PrologEngine();
        compiled.Install(engine);
        Assert.True(engine.Query("findall(X,colours:colour(X),[red,blue])").Prove());
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void LanguageModeAndInitialFlagsAreValidated(PrologLanguageMode mode)
    {
        var flags = new PrologFlagOverrides { DoubleQuotes = DoubleQuotesMode.Atom };
        using var compiled = new LoadedProgram("p(\"hello\").", mode, flags);
        var engine = new PrologEngine(mode, flags);
        compiled.Install(engine);
        Assert.True(engine.Query("p(hello)").Prove());
        Assert.Throws<PrologException>(() => compiled.Install(new PrologEngine(mode)));
    }

    [Fact]
    public void MalformedSourceReturnsPositionedDiagnosticsAndNoAssembly()
    {
        using var stream = new MemoryStream();
        var diagnostics = IlAssemblyEmitter.Emit([("broken.pl", "p( .")], "Broken", stream);
        Assert.Contains(
            diagnostics,
            diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.FileName == "broken.pl" && diagnostic.Span.Line == 1
        );
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void EmissionIsDeterministicAndContainsExecutableBlocks()
    {
        using var first = new MemoryStream();
        using var second = new MemoryStream();
        IlAssemblyEmitter.Emit([("test.pl", "p(a).")], "Deterministic", first);
        IlAssemblyEmitter.Emit([("test.pl", "p(a).")], "Deterministic", second);
        Assert.Equal(first.ToArray(), second.ToArray());
        first.Position = 0;
        using var pe = new PEReader(first);
        var metadata = pe.GetMetadataReader();
        Assert.NotEqual(0, pe.PEHeaders.CorHeader!.EntryPointTokenOrRelativeVirtualAddress);
        Assert.Contains(
            metadata.MethodDefinitions,
            method => metadata.GetString(metadata.GetMethodDefinition(method).Name) == "Block0"
        );
        Assert.DoesNotContain(
            metadata.AssemblyReferences,
            reference =>
                metadata.GetString(metadata.GetAssemblyReference(reference).Name).Contains("CSharp", StringComparison.Ordinal)
        );
        Assert.Contains(
            metadata.MemberReferences,
            reference => metadata.GetString(metadata.GetMemberReference(reference).Name) == "GetConstant"
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StrictIsoReviewCorpusPassesThroughCompiledAndConsultedCalls(bool consultInvoker)
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "strict_iso_review.pl"));
        const string invoker = "iso_review_invoke(Goal) :- call(Goal).";
        if (consultInvoker)
        {
            source = source.Replace(invoker, string.Empty, StringComparison.Ordinal);
        }
        using var compiled = new LoadedProgram(source, PrologLanguageMode.StrictIso);
        var engine = new PrologEngine(PrologLanguageMode.StrictIso);
        compiled.Install(engine);
        Assert.True(
            engine
                .ConsultText("bytecode_review(Name) :- run_iso_review_case(Name).\n" + (consultInvoker ? invoker : string.Empty))
                .Success
        );
        var names = engine.Query("iso_review_case(Name,_)").Solutions().Select(solution => solution["Name"].ToString()).ToArray();
        Assert.Equal(125, names.Length);
        foreach (var name in names)
        {
            Assert.True(engine.Query($"run_iso_review_case({name})").Prove(), name);
            Assert.Single(engine.Query($"bytecode_review({name})").Solutions());
        }
    }
}
