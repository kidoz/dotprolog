using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

public sealed class HostBoundaryTests
{
    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    public void AtomicClosuresEnforceMaximumArity(int arity)
    {
        using var engine = new PrologEngine();
        engine.ConsultOrThrow($"allowed({string.Join(",", Enumerable.Repeat("_", 255))}).", "arity.pl");
        var goal = $"length(L,{arity}), '$add_args'(allowed,L,G), call(G)";
        if (arity == 255)
            Assert.True(engine.Query(goal).Prove());
        else
            Assert.True(engine.Query($"catch(({goal}, fail), error(representation_error(max_arity),_), true)").Prove());
    }

    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    public void HostCompoundInputsEnforceMaximumArity(int arity)
    {
        using var engine = new PrologEngine();
        engine.ConsultOrThrow("identity(X,X).", "host.pl");
        var host = new PrologHost(engine.Machine);
        var predicate = host.Bind("identity", 2);
        var arguments = Enumerable.Repeat(PrologInput.Atom("a"), arity).ToArray();
        var input = PrologInput.Compound("f", arguments);
        if (arity == 255)
        {
            var answer = Assert.IsType<PrologCompound>(Assert.Single(host.CallOnce(predicate, input, PrologInput.Output)!));
            Assert.Equal(arity, answer.Arguments.Count);
        }
        else
        {
            var error = Assert.Throws<PrologException>(() => host.CallOnce(predicate, input, PrologInput.Output));
            Assert.Contains("representation_error(max_arity)", error.Message, StringComparison.Ordinal);
            Assert.False(engine.Machine.HasAlternatives);
        }
    }

    [Fact]
    public void HandlesFromAnotherMachineOrWithChangedIdentityAreRejectedBeforeExecution()
    {
        using var left = new PrologEngine();
        using var right = new PrologEngine();
        left.ConsultOrThrow("left.", "left.pl");
        right.ConsultOrThrow("right.", "right.pl");
        var first = new PrologHost(left.Machine);
        var second = new PrologHost(right.Machine);
        var handle = first.Bind("left", 0);
        var rightHandle = second.Bind("right", 0);
        Assert.Equal(handle.FunctorId, rightHandle.FunctorId);
        Assert.Throws<ArgumentException>(() => second.Prove(handle));
        Assert.Throws<ArgumentException>(() => second.CallOnce(handle));
        Assert.Throws<ArgumentException>(() => second.CallAll(handle).ToArray());
        Assert.Throws<ArgumentException>(() => first.Prove(default));
        Assert.Throws<ArgumentException>(() => first.Prove(handle with { Name = "other" }));
        Assert.Throws<ArgumentException>(() => first.Prove(handle with { FunctorId = int.MaxValue }));
        Assert.True(first.Prove(handle));
        Assert.True(second.Prove(rightHandle));
        Assert.True(new PrologHost(left.Machine).Prove(handle));
    }

    [Fact]
    public void BoundHandlesRetainTheCanonicalAtomName()
    {
        using var engine = new PrologEngine();
        engine.ConsultOrThrow("'\uFFFD'.", "unicode.pl");
        var host = new PrologHost(engine.Machine);
        var predicate = host.Bind("\uD800", 0);
        Assert.Equal("\uFFFD", predicate.Name);
        Assert.True(host.Prove(predicate));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneShotHostCallsDiscardAlternativesAndKeepDetachedAnswers(bool outputs)
    {
        using var engine = new PrologEngine();
        engine.ConsultOrThrow("item(first). item(second).", "host.pl");
        var host = new PrologHost(engine.Machine);
        var predicate = host.Bind("item", 1);
        if (outputs)
            Assert.Equal("first", Assert.Single(host.CallOnce(predicate, PrologInput.Output)!).ToString());
        else
            Assert.True(host.Prove(predicate, PrologInput.Output));
        Assert.False(engine.Machine.HasAlternatives);
        Assert.Equal(RunResult.Failure, engine.Machine.Redo());
        Assert.Equal(2, host.CallAll(predicate, PrologInput.Output).Count());
    }

    [Theory]
    [InlineData("prove")]
    [InlineData("first")]
    [InlineData("enumeration")]
    public void QueryWrappersDiscardSuspendedState(string operation)
    {
        using var engine = new PrologEngine();
        var query = engine.Query("(X=first; X=second)");
        if (operation == "prove")
            Assert.True(query.Prove());
        else if (operation == "first")
            Assert.Equal("first", query.FirstOrDefault()!["X"].ToString());
        else
        {
            using var answers = query.Solutions().GetEnumerator();
            Assert.True(answers.MoveNext());
            Assert.Equal("first", answers.Current["X"].ToString());
        }
        Assert.False(engine.Machine.HasAlternatives);
        Assert.Equal(RunResult.Failure, engine.Machine.Redo());
        Assert.Equal(2, query.Solutions().Count());
    }

    [Theory]
    [InlineData("throw(ball)")]
    [InlineData("cancel_now")]
    [InlineData("host_fault")]
    public void EscapingDispatchCannotResumeAnEarlierSolution(string escape)
    {
        using var engine = new PrologEngine();
        using var cancellation = new CancellationTokenSource();
        engine.Program.Builtins.Register(
            "cancel_now",
            0,
            _ =>
            {
                cancellation.Cancel();
                return true;
            }
        );
        engine.Program.Builtins.Register("host_fault", 0, _ => throw new IOException("host fault"));
        engine.Machine.CancellationToken = cancellation.Token;
        Assert.Equal(RunResult.Success, engine.RunGoal($"(X=first; {escape}; X=third)", out _));
        Assert.True(engine.Machine.HasAlternatives);
        if (escape == "throw(ball)")
            Assert.Throws<PrologException>(() => engine.Machine.Redo());
        else if (escape == "cancel_now")
            Assert.Throws<OperationCanceledException>(() => engine.Machine.Redo());
        else
            Assert.Throws<IOException>(() => engine.Machine.Redo());
        Assert.False(engine.Machine.IsRunning);
        Assert.False(engine.Machine.HasAlternatives);
        Assert.Equal(RunResult.Failure, engine.Machine.Redo());
        engine.Machine.CancellationToken = default;
        Assert.True(engine.Query("true").Prove());
    }

    [Theory]
    [InlineData("atom(_)", false)]
    [InlineData("target", false)]
    [InlineData("atom(_)", true)]
    [InlineData("target", true)]
    public void CancellationUnwindsNestedCaptureAndStreamScopes(string sink, bool nested)
    {
        var directory = Directory.CreateTempSubdirectory("dotprolog-output-abort-");
        try
        {
            using var engine = new PrologEngine();
            using var output = new StringWriter();
            engine.Output = output;
            var path = Path.Combine(directory.FullName, "output.txt").Replace("\\", "/", StringComparison.Ordinal);
            Assert.True(engine.Query($"open('{path}', write, _, [alias(target)])").Prove());
            using var cancellation = new CancellationTokenSource();
            engine.Program.Builtins.Register(
                "cancel_now",
                0,
                _ =>
                {
                    cancellation.Cancel();
                    return true;
                }
            );
            engine.Machine.CancellationToken = cancellation.Token;
            var goal = nested ? "with_output_to(atom(_), with_output_to(target,cancel_now))" : "cancel_now";
            Assert.Throws<OperationCanceledException>(() => engine.Query($"with_output_to({sink}, {goal})").Prove());
            Assert.Same(engine.Machine.Streams.UserOutput, engine.Machine.Streams.CurrentOutput);
            engine.Machine.CancellationToken = default;
            Assert.True(engine.Query("write(after), close(target)").Prove());
            Assert.Equal("after", output.ToString());
            Assert.Equal("", File.ReadAllText(path));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbortedTemporaryOutputRestoresAUsableSelection(bool halt)
    {
        var directory = Directory.CreateTempSubdirectory("dotprolog-output-selection-");
        try
        {
            using var engine = new PrologEngine();
            using var output = new StringWriter();
            engine.Output = output;
            var path = Path.Combine(directory.FullName, "output.txt").Replace("\\", "/", StringComparison.Ordinal);
            Assert.True(engine.Query($"open('{path}', write, S, [alias(selected)]), set_output(S)").Prove());
            var selected = engine.Machine.Streams.CurrentOutput;
            engine.Program.Builtins.Register("host_fault", 0, _ => throw new IOException("host fault"));
            if (halt)
            {
                Assert.Equal(RunResult.Halted, engine.RunGoal("with_output_to(atom(_),halt(7))", out _));
                Assert.Equal(7, engine.Machine.ExitCode);
                engine.Machine.DiscardCall();
                Assert.Equal(7, engine.Machine.ExitCode);
            }
            else
                Assert.Throws<IOException>(() => engine.RunGoal("with_output_to(atom(_),host_fault)", out _));
            Assert.Same(halt ? engine.Machine.Streams.UserOutput : selected, engine.Machine.Streams.CurrentOutput);
            Assert.Equal(RunResult.Failure, engine.Machine.Redo());
            if (halt)
            {
                Assert.False(selected.IsOpen);
                Assert.True(engine.Query("write(restored)").Prove());
                Assert.Equal("restored", output.ToString());
            }
            else
            {
                Assert.True(engine.Query("write(restored), set_output(user_output), close(selected)").Prove());
                Assert.Equal("restored", File.ReadAllText(path));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
