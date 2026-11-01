using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

public sealed class DynamicCodeReclamationTests
{
    private const string Churn = """
        :- dynamic item/1.
        item(live).
        churn(0, _) :- !.
        churn(N, I) :- assertz(item(I)), retract(item(I)), M is N-1, J is I+1, churn(M, J).
        """;

    [Fact]
    public void CodeAndConstantHighWaterPlateauAcrossBatchesOfUniqueFixnums()
    {
        using var engine = new PrologEngine();
        engine.ConsultOrThrow(Churn, "churn.pl");
        var host = new PrologHost(engine.Machine);
        var churn = host.Bind("churn", 2);
        var live = host.Bind("item", 1);
        var code = engine.Program.CodeLength;
        var constants = engine.Program.ConstantCount;
        for (var batch = 0; batch < 20; batch++)
        {
            Assert.True(host.Prove(churn, PrologInput.Integer(256), PrologInput.Integer(batch * 256 + 1)));
            Assert.True(host.Prove(live, PrologInput.Atom("live")));
            Assert.Equal(code, engine.Program.CodeLength);
            Assert.Equal(constants, engine.Program.ConstantCount);
        }
    }

    [Fact]
    public void HostCompiledGoalsAndPublicClauseCompilationKeepStableAddressesAndConstants()
    {
        using var engine = new PrologEngine();
        engine.ConsultOrThrow(Churn + "\nmarker(314159).", "pinned.pl");
        var goal = engine.CompileGoal("item(live), marker(314159)", out var diagnostics);
        Assert.Empty(diagnostics);
        engine.Machine.BeginCall();
        Cell clause = PrologInput
            .Compound(":-", PrologInput.Atom("raw"), PrologInput.Compound("marker", PrologInput.Integer(314159)))
            .Build(engine.Machine);
        var raw = engine.CompileClause(engine.Machine, clause, out _);
        var host = new PrologHost(engine.Machine);
        var churn = host.Bind("churn", 2);
        var code = engine.Program.CodeLength;
        var constants = engine.Program.ConstantCount;
        for (var batch = 0; batch < 10; batch++)
        {
            Assert.True(host.Prove(churn, PrologInput.Integer(128), PrologInput.Integer(batch * 128 + 1)));
            Assert.Equal(RunResult.Success, engine.Machine.Run(goal));
            Assert.Equal(RunResult.Success, engine.Machine.Run(raw));
            Assert.Equal(code, engine.Program.CodeLength);
            Assert.Equal(constants, engine.Program.ConstantCount);
        }
    }

    [Fact]
    public void SuspendedReaderProtectsSourceClauseCodeAndConstantsAcrossRetractionAndReplacement()
    {
        using var engine = new PrologEngine();
        engine.ConsultOrThrow(
            """
            :- dynamic p/2.
            p(1, first). p(2, second). p(3, third).
            replace :- retractall(p(_,_)), assertz(p(99,new)).
            """,
            "reader.pl"
        );
        using var reader = new Machine(engine.Program);
        var p = engine.Program.Symbols.InternFunctor("p", 2);
        reader.BeginCall();
        Assert.Equal(RunResult.Success, reader.Call(p, [reader.CreateVariable(), reader.CreateVariable()]));
        var host = new PrologHost(engine.Machine);
        Assert.True(host.Prove(host.Bind("replace", 0)));
        Assert.Equal(RunResult.Success, reader.Redo());
        Assert.Equal("second", reader.Symbols.AtomName(reader.Argument(1).Index));
        Assert.Equal(RunResult.Success, reader.Redo());
        Assert.Equal("third", reader.Symbols.AtomName(reader.Argument(1).Index));
        Assert.Equal(RunResult.Failure, reader.Redo());
        Assert.True(host.Prove(host.Bind("p", 2), PrologInput.Integer(99), PrologInput.Atom("new")));
        // The first replacement can now move into the released source-clause holes.
        Assert.True(host.Prove(host.Bind("replace", 0)));
        Assert.True(host.Prove(host.Bind("p", 2), PrologInput.Integer(99), PrologInput.Atom("new")));
        var code = engine.Program.CodeLength;
        var constants = engine.Program.ConstantCount;
        for (var i = 0; i < 10; i++)
        {
            Assert.True(host.Prove(host.Bind("replace", 0)));
            Assert.True(host.Prove(host.Bind("p", 2), PrologInput.Integer(99), PrologInput.Atom("new")));
            Assert.Equal(code, engine.Program.CodeLength);
            Assert.Equal(constants, engine.Program.ConstantCount);
        }
    }

    [Theory]
    [InlineData(PrologLanguageMode.StrictIso, "assertz((broken(X) :- length(X,1)))")]
    [InlineData(PrologLanguageMode.Modern, "assertz(fixed(2))")]
    public void FailedCompilationOrPublicationDoesNotConsumePrivateCodeOrConstants(PrologLanguageMode mode, string assertion)
    {
        using var engine = new PrologEngine(mode);
        engine.ConsultOrThrow($"fixed(1). attempt :- catch({assertion}, error(_, _), true).", "failure.pl");
        var host = new PrologHost(engine.Machine);
        var attempt = host.Bind("attempt", 0);
        var code = engine.Program.CodeLength;
        var constants = engine.Program.ConstantCount;
        for (var i = 0; i < 20; i++)
        {
            Assert.True(host.Prove(attempt));
            Assert.Equal(code, engine.Program.CodeLength);
            Assert.Equal(constants, engine.Program.ConstantCount);
            Assert.True(host.Prove(host.Bind("fixed", 1), PrologInput.Integer(1)));
        }
    }

    [Fact]
    public void SelfRetractingClauseKeepsItsContinuationAndConstantsUntilItReturns()
    {
        using var engine = new PrologEngine();
        engine.ConsultOrThrow(
            Churn
                + "\n"
                + """
                :- dynamic self/1.
                self(Result) :-
                    retractall(self(_)), churn(128,1),
                    (fail -> Result=wrong ; Result=old),
                    catch(throw(ball(old)), ball(Result), true).
                """,
            "self.pl"
        );
        var host = new PrologHost(engine.Machine);
        PrologValue result = Assert.Single(host.CallOnce(host.Bind("self", 1), PrologInput.Output)!);
        Assert.Equal("old", result.ToString());
        Assert.False(host.Prove(host.Bind("self", 1), PrologInput.Output));
        Assert.Equal("old", result.ToString());
    }

    [Fact]
    public void AbolishAndRedeclareReuseTheTrampolineForTheSamePredicate()
    {
        using var engine = new PrologEngine();
        engine.ConsultOrThrow("make :- assertz(transient(42)). drop :- abolish(transient/1).", "abolish.pl");
        var host = new PrologHost(engine.Machine);
        var make = host.Bind("make", 0);
        var drop = host.Bind("drop", 0);
        Assert.True(host.Prove(make));
        Assert.True(host.Prove(drop));
        engine.Machine.BeginCall();
        var code = engine.Program.CodeLength;
        var constants = engine.Program.ConstantCount;
        for (var i = 0; i < 50; i++)
        {
            Assert.True(host.Prove(make));
            Assert.True(host.Prove(drop));
            engine.Machine.BeginCall();
            Assert.Equal(code, engine.Program.CodeLength);
            Assert.Equal(constants, engine.Program.ConstantCount);
        }
    }

    [Theory]
    [InlineData("(fail ; X = branch)", "branch")]
    [InlineData("(fail -> X=wrong ; X=conditional)", "conditional")]
    [InlineData("(X=soft *-> true ; fail)", "soft")]
    [InlineData("(\\+ fail, X=negation)", "negation")]
    [InlineData("catch(throw(ball(caught)), ball(X), true)", "caught")]
    [InlineData("(freeze(Y, X=woken), Y=bound)", "woken")]
    public void ReusedRulesKeepBranchesCatchAndWakeContinuations(string body, string expected)
    {
        using var engine = new PrologEngine();
        engine.ConsultOrThrow(
            $"""
            :- dynamic p/1.
            make :- assertz((p(X) :- {body})).
            drop :- retractall(p(_)).
            """,
            "rules.pl"
        );
        var host = new PrologHost(engine.Machine);
        var make = host.Bind("make", 0);
        var drop = host.Bind("drop", 0);
        var p = host.Bind("p", 1);
        for (var i = 0; i < 10; i++)
        {
            Assert.True(host.Prove(make));
            Assert.Equal(expected, Assert.Single(host.CallOnce(p, PrologInput.Output)!).ToString());
            Assert.True(host.Prove(drop));
            engine.Machine.BeginCall();
        }
    }

    [Fact]
    public void CustomCompilerForwardingThePublicContractKeepsItsCodePinned()
    {
        using var engine = new PrologEngine();
        engine.ConsultOrThrow(Churn, "custom.pl");
        engine.Program.RuntimeCompiler = new PinnedCompiler(engine);
        var host = new PrologHost(engine.Machine);
        var code = engine.Program.CodeLength;
        var constants = engine.Program.ConstantCount;
        Assert.True(host.Prove(host.Bind("churn", 2), PrologInput.Integer(128), PrologInput.Integer(1)));
        Assert.True(host.Prove(host.Bind("item", 1), PrologInput.Atom("live")));
        Assert.True(engine.Program.CodeLength > code);
        Assert.True(engine.Program.ConstantCount > constants);
    }

    private sealed class PinnedCompiler(IRuntimeCompiler inner) : IRuntimeCompiler
    {
        public int CompileClause(Machine machine, Cell clause, out int functorId) =>
            inner.CompileClause(machine, clause, out functorId);

        public int CompileControlGoal(Machine machine, Cell goal, Span<Cell> arguments, out int argumentCount) =>
            inner.CompileControlGoal(machine, goal, arguments, out argumentCount);

        public void ConsultFile(Machine machine, string path) => inner.ConsultFile(machine, path);

        public void EnsureLoadedFile(Machine machine, string path) => inner.EnsureLoadedFile(machine, path);

        public bool TryReadTerm(
            Machine machine,
            TextReader input,
            ref string buffer,
            out Cell term,
            out Cell variableNames,
            out Cell variables,
            out Cell singletons
        ) => inner.TryReadTerm(machine, input, ref buffer, out term, out variableNames, out variables, out singletons);
    }
}
