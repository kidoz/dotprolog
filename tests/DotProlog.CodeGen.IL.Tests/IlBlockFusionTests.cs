using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotProlog.Compiler;

namespace DotProlog.CodeGen.IL.Tests;

public sealed class IlBlockFusionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedPartialHeadMatchDoesNotWakeTheDiscardedBinding(bool fuseBlocks)
    {
        using var program = new LoadedProgram("p(f(a),a,a). p(f(b),b,b).", fuseBlocks: fuseBlocks);
        using var output = new StringWriter();
        var engine = new PrologEngine { Output = output };
        program.Install(engine);
        Assert.True(engine.Query("freeze(X,write(X)), p(f(X),X,b)").Prove());
        Assert.Equal("b", output.ToString());
    }

    [Fact]
    public void StraightLineInstructionsShareMethodsAndShrinkTheAssembly()
    {
        // Keep the size difference larger than PE section-alignment padding.
        var source = string.Join("\n", Enumerable.Range(0, 20).Select(index => $"p{index}(f(a,b), [a,b], a, b)."));
        using var fused = new MemoryStream();
        using var reference = new MemoryStream();
        Assert.Empty(IlAssemblyEmitter.Emit([("test.pl", source)], "BlockTest", fused));
        Assert.Empty(IlAssemblyEmitter.Emit([("test.pl", source)], "BlockTest", reference, fuseBlocks: false));
        fused.Position = 0;
        reference.Position = 0;
        using var fusedPe = new PEReader(fused);
        using var referencePe = new PEReader(reference);
        Assert.True(
            fusedPe.GetMetadataReader().MethodDefinitions.Count < referencePe.GetMetadataReader().MethodDefinitions.Count
        );
        Assert.True(fused.Length < reference.Length);
    }

    [Theory]
    [InlineData("p(a,a). p(b,b).", "findall(X,p(X,b),[b])")]
    [InlineData("p(f(a,b),a). p(f(c,d),c).", "findall(X,p(f(X,d),c),[c])")]
    [InlineData("p(X) :- (X=a;X=b), (X=b -> true; fail). p(c).", "findall(X,p(X),[b,c])")]
    [InlineData("p(X) :- catch((X=a;throw(ball)),ball,X=b).", "findall(X,p(X),[a,b])")]
    [InlineData("p(X) :- ((X=a;X=b) *-> true;X=c).", "findall(X,p(X),[a,b])")]
    [InlineData("p(0) :- !. p(N) :- M is N-1, p(M).", "p(100000)")]
    [InlineData("p(X,X).", "set_prolog_flag(occurs_check,true), \\+ p(A,f(A)), p(a,a)")]
    [InlineData("p(a). p(b).", "findall(X,setup_call_cleanup(true,p(X),true),[a,b])")]
    public void FusedAndInstructionBlocksPreserveSolutions(string source, string goal)
    {
        foreach (var fused in new[] { false, true })
        {
            using var program = new LoadedProgram(source, fuseBlocks: fused);
            var engine = new PrologEngine();
            program.Install(engine);
            Assert.True(engine.Query(goal).Prove());
        }
    }

    [Theory]
    [InlineData("bind(a).", "freeze(X,write(w)), bind(X), write(done)", "wdone")]
    [InlineData("bind(a) :- !. bind(b).", "freeze(X,fail), \\+ bind(X), write(done)", "done")]
    [InlineData("bind(a) :- write(body).", "freeze(X,write(w)), bind(X)", "wbody")]
    [InlineData("bind(a) :- call(write(body)).", "freeze(X,write(w)), bind(X)", "wbody")]
    [InlineData("bind(a) :- other. other :- write(body).", "freeze(X,write(w)), bind(X)", "wbody")]
    [InlineData("bind(a) :- (true -> write(body); fail).", "freeze(X,write(w)), bind(X)", "wbody")]
    [InlineData("bind(a) :- catch(write(body),_,fail).", "freeze(X,write(w)), bind(X)", "wbody")]
    [InlineData("bind(a). bind(b).", "freeze(X,write(X)), findall(X,bind(X),L), write(L)", "ab[a,b]")]
    [InlineData("bind(a). bind(b).", "freeze(X,(X=b,write(X))), bind(X), write(done)", "bdone")]
    [InlineData("bind(X) :- !, X=a. bind(b).", "findall(X,bind(X),L), write(L)", "[a]")]
    [InlineData("bind(_) :- throw(ball). bind(b).", "catch(bind(_),ball,write(caught))", "caught")]
    [InlineData("bind(a).", "catch((freeze(X,throw(ball)),bind(X)),ball,write(caught))", "caught")]
    [InlineData("bind(a).", "freeze(X,member(Y,[1,2])), bind(X), write(Y), Y=2", "12")]
    public void WakeupResumesAtItsOwnInstructionWithoutReplayingTheFusedPrefix(string source, string goal, string expected)
    {
        foreach (var fused in new[] { false, true })
        {
            using var program = new LoadedProgram(source, fuseBlocks: fused);
            using var output = new StringWriter();
            var engine = new PrologEngine { Output = output };
            program.Install(engine);
            Assert.True(engine.Query(goal).Prove());
            Assert.Equal(expected, output.ToString());
        }
    }

    [Fact]
    public void HostRedoCrossesCompiledAndConsultedContinuationsInOrder()
    {
        foreach (var fused in new[] { false, true })
        {
            using var program = new LoadedProgram("p(a). p(b). bridge(X) :- consulted(X), true.", fuseBlocks: fused);
            var engine = new PrologEngine();
            program.Install(engine);
            engine.ConsultOrThrow("consulted(X) :- p(X).", "runtime.pl");
            Assert.Collection(
                engine.Query("bridge(X)").Solutions(),
                solution => Assert.Equal("a", solution["X"].ToString()),
                solution => Assert.Equal("b", solution["X"].ToString())
            );
        }
    }
}
