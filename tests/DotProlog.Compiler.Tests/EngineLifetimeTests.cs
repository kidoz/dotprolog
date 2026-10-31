using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

public sealed class EngineLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposalFlushesProgramStreamsAndLeavesHostStreamsOpen(bool disposeMachine)
    {
        var directory = Directory.CreateTempSubdirectory("dotprolog-dispose-");
        try
        {
            var path = Path.Combine(directory.FullName, "output.txt").Replace("\\", "/", StringComparison.Ordinal);
            using var output = new StringWriter();
            using var error = new StringWriter();
            using var input = new StringReader("input");
            using var engine = new PrologEngine
            {
                Output = output,
                Error = error,
                Input = input,
            };
            Assert.True(engine.Query($"open('{path}', write, S, [alias(file)]), write(S, buffered)").Prove());
            Assert.NotNull(engine.Machine.Streams.ByAlias("file"));

            if (disposeMachine)
            {
                engine.Machine.Dispose();
                engine.Machine.Dispose();
            }
            else
            {
                engine.Dispose();
                engine.Dispose();
            }

            Assert.Equal("buffered", File.ReadAllText(path));
            Assert.Null(engine.Machine.Streams.ByAlias("file"));
            output.Write("still open");
            error.Write("still open");
            Assert.Equal('i', input.Read());
            Assert.Throws<ObjectDisposedException>(() => engine.Query("true").Prove());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void CancellationStopsASucceedingLoopWithoutClosingStreamsFromTheCaller()
    {
        using var engine = new PrologEngine();
        engine.ConsultOrThrow("loop :- loop.", "loop.pl");
        using var cancellation = new CancellationTokenSource();
        engine.Machine.CancellationToken = cancellation.Token;
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        Assert.Throws<OperationCanceledException>(() => engine.Query("loop").Prove());
        Assert.False(engine.Machine.IsRunning);
        engine.Machine.CancellationToken = default;
        Assert.True(engine.Query("true").Prove());
    }
}
