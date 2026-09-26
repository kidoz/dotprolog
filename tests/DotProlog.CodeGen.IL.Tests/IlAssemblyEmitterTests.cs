using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.CodeGen.IL.Tests;

public sealed class IlAssemblyEmitterTests
{
    [Theory]
    [InlineData(
        "p(a,1). p(_,2). p(b,3). p(a,4).",
        "findall(V,p(a,V),[1,2,4]), findall(V,p(b,V),[2,3]), findall(V,p(c,V),[2]), findall(V,p(_,V),[1,2,3,4])"
    )]
    [InlineData("p(a). p(b).", "\\+ p(c), X=b, p(X)")]
    [InlineData("p(1,int). p(1.0,float).", "findall(V,p(1,V),[int]), findall(V,p(1.0,V),[float])")]
    [InlineData(
        "p([],empty). p([_|_],list). p(f(_),f). p(f(_,_),ff). p(atom,atom).",
        "p([],empty), p([a],list), p(f(a),f), p(f(a,b),ff), p(atom,atom), \\+ p(g(a),_)"
    )]
    [InlineData(
        ":- set_prolog_flag(double_quotes,string). p(\"a\",string). p(a,atom).",
        "atom_string(a,S), findall(V,p(S,V),[string]), findall(V,p(a,V),[atom])"
    )]
    [InlineData(
        "p(115292150460684697600,big). p(1r3,rational). p(a,atom).",
        "findall(V,p(115292150460684697600,V),[big]), findall(V,p(1r3,V),[rational])"
    )]
    [InlineData(
        "p(a,1) :- !. p(a,2). p(b,3).",
        "findall(V,p(a,V),[1]), findall(V,p(b,V),[3]), findall(V,(p(a,V);V=outer),[1,outer])"
    )]
    [InlineData("p(f(a),a,a). p(f(b),b,b).", "findall(X,p(f(X),X,b),[b])")]
    [InlineData("p(a) :- throw(ball). p(b).", "catch(p(a),ball,true), p(b)")]
    [InlineData(
        "p(a,T) :- b_setval(g,changed), setarg(1,T,changed), fail. p(b,T) :- arg(1,T,old), b_getval(g,old).",
        "b_setval(g,old), T=t(old), p(_,T), arg(1,T,old), b_getval(g,old)"
    )]
    [InlineData(
        "p(a). p(b). choose(X) :- (p(X) *-> true; X=else).",
        "findall(X,choose(X),[a,b]), findall(X,(choose(X);X=outer),[a,b,outer])"
    )]
    public void IndexedAndLinearIlPreserveClauseSelection(string source, string goal)
    {
        using (var table = new LoadedProgram(source, linearVariableFallback: false))
        {
            var engine = new PrologEngine();
            table.Install(engine);
            Assert.True(engine.Query(goal).Prove());
        }
        foreach (var indexed in new[] { false, true })
        {
            foreach (var fused in new[] { false, true })
            {
                using var compiled = new LoadedProgram(source, fuseBlocks: fused, indexFirstArgument: indexed);
                var engine = new PrologEngine();
                // Relocation must not rely on the compiling engine's symbol or table IDs.
                engine.ConsultOrThrow("unrelated(z). unrelated(y). extra(q(r)).", "existing.pl");
                compiled.Install(engine);
                Assert.True(engine.Query(goal).Prove());
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexedDeterministicCallRunsCleanupBeforeReturning(bool fused)
    {
        using var compiled = new LoadedProgram("p(a). p(b). p(c).", fuseBlocks: fused);
        var engine = new PrologEngine();
        compiled.Install(engine);
        Assert.True(engine.Query("setup_call_cleanup(true,p(b),Done=yes), Done==yes").Prove());
        Assert.True(
            engine
                .Query(
                    "findall(X-D,(setup_call_cleanup(true,p(X),Done=yes),(var(Done)->D=pending;D=Done)),[a-pending,b-pending,c-yes])"
                )
                .Prove()
        );
    }

    [Fact]
    public void IndexedPreparationUsesTheSourcePositionSnapshot()
    {
        using var compiled = new LoadedProgram(
            "p(a). p(b). :- findall(X,p(X),L), write(L). p(c). :- findall(X,p(X),L), write(L)."
        );
        using var output = new StringWriter();
        var engine = new PrologEngine { Output = output };
        compiled.Install(engine);
        Assert.Equal("[a,b][a,b,c]", output.ToString());
        Assert.True(engine.Query("findall(X,p(X),[a,b,c])").Prove());
    }

    [Fact]
    public void VersionOneInstallationImagesRemainReadable()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(InstallationImage.Magic);
            writer.Write(1);
            writer.Write((int)PrologLanguageMode.Modern);
            writer.Write((int)new PrologEngine().Program.InitialDoubleQuotes);
            // Version one: blocks, functors, builtins, constants, modules, preparation,
            // predicates, dynamic predicates, and initialization; no static-index section.
            for (var i = 0; i < 9; i++)
            {
                writer.Write(0);
            }
        }
        var image = Convert.ToBase64String(stream.ToArray());
        Assert.Empty(IlProgramHost.Install(new PrologEngine(), image, []));
        Assert.Equal(0, IlProgramHost.Run(image, []));
    }

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
        IlAssemblyEmitter.Emit([("test.pl", "p(a). p(b).")], "Deterministic", first);
        IlAssemblyEmitter.Emit([("test.pl", "p(a). p(b).")], "Deterministic", second);
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
