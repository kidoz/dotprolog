using System.Text;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

/// <summary>Host I/O failures remain catchable ISO errors instead of escaping the Prolog machine.</summary>
public sealed class StreamSystemErrorTests
{
    [Theory]
    [InlineData("get_char(_)")]
    [InlineData("peek_char(_)")]
    [InlineData("get_code(_)")]
    [InlineData("peek_code(_)")]
    [InlineData("at_end_of_stream")]
    [InlineData("stream_property(user_input, end_of_stream(_))")]
    [InlineData("read(_)")]
    public void InputFailuresRaiseCatchableSystemError(string operation)
    {
        var engine = new PrologEngine { Input = new FailingReader() };

        Assert.Equal(
            RunResult.Success,
            engine.RunGoal($"catch({operation}, error(system_error, _), true)", out IReadOnlyList<Diagnostic> diagnostics)
        );
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("write(x)")]
    [InlineData("print(x)")]
    [InlineData("writeq(x)")]
    [InlineData("writeln(x)")]
    [InlineData("write_canonical(x)")]
    [InlineData("write_term(x, [])")]
    [InlineData("put_char(x)")]
    [InlineData("put_code(120)")]
    [InlineData("nl")]
    [InlineData("flush_output")]
    [InlineData("close(user_output)")]
    [InlineData("format(x)")]
    [InlineData("format('~w', [x])")]
    [InlineData("tab(1)")]
    public void OutputFailuresRaiseCatchableSystemError(string operation)
    {
        var engine = new PrologEngine { Output = new FailingWriter() };

        Assert.Equal(
            RunResult.Success,
            engine.RunGoal($"catch({operation}, error(system_error, _), true)", out IReadOnlyList<Diagnostic> diagnostics)
        );
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ForceCloseIgnoresAStandardOutputFlushFailure()
    {
        var engine = new PrologEngine { Output = new FailingWriter() };

        Assert.Equal(
            RunResult.Success,
            engine.RunGoal("close(user_output, [force(true)])", out IReadOnlyList<Diagnostic> diagnostics)
        );
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("false", "catchable")]
    [InlineData("true", "forced")]
    public void CloseNormalizesDisposedOutputAccordingToForce(string force, string expected)
    {
        var engine = new PrologEngine { Output = new DisposedWriter() };
        string goal =
            force == "true"
                ? "close(user_output, [force(true)]), write(forced)"
                : "catch(close(user_output, [force(false)]), error(system_error, _), true), write(catchable)";

        Assert.Equal(RunResult.Success, engine.RunGoal(goal, out IReadOnlyList<Diagnostic> diagnostics));
        Assert.Equal(expected, engine.Output.ToString());
        Assert.Empty(diagnostics);
    }

    private sealed class FailingReader : TextReader
    {
        public override int Peek() => throw new IOException("peek failed");

        public override int Read() => throw new IOException("read failed");

        public override string? ReadLine() => throw new IOException("line read failed");
    }

    private sealed class FailingWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => throw new IOException("character write failed");

        public override void Write(string? value) => throw new IOException("text write failed");

        public override void Flush() => throw new IOException("flush failed");
    }

    private sealed class DisposedWriter : StringWriter
    {
        public override void Flush() => throw new ObjectDisposedException(nameof(DisposedWriter));
    }
}
