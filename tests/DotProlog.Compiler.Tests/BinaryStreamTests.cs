using System.Diagnostics;

namespace DotProlog.Compiler.Tests;

/// <summary>ISO binary streams, byte input and output, type permissions, and byte domains.</summary>
public sealed class BinaryStreamTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dotprolog-binary-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name).Replace("\\", "/", StringComparison.Ordinal);

    [Fact]
    public void WritesPeeksAndReadsEveryByteRange()
    {
        var path = Path("bytes.bin");

        Assert.Equal(
            "[0,1,127,128,128,255,-1]\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, Out, [type(binary)]),
                  put_byte(Out, 0), put_byte(Out, 1), put_byte(Out, 127),
                  put_byte(Out, 128), put_byte(Out, 255),
                close(Out),
                open('{path}', read, In, [type(binary)]),
                  get_byte(In, A), get_byte(In, B), get_byte(In, C),
                  peek_byte(In, P), get_byte(In, D), get_byte(In, E), get_byte(In, End),
                close(In),
                write([A,B,C,P,D,E,End]), nl
                """
            )
        );
    }

    [Fact]
    public void BinaryAppendPreservesExistingBytes()
    {
        var path = Path("append.bin");

        Assert.Equal(
            "[10,20]\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, First, [type(binary)]), put_byte(First, 10), close(First),
                open('{path}', append, Last, [type(binary)]), put_byte(Last, 20), close(Last),
                open('{path}', read, In, [type(binary)]),
                  get_byte(In, A), get_byte(In, B),
                close(In),
                write([A,B]), nl
                """
            )
        );
    }

    [Fact]
    public void ResetReadsBytesAppendedAfterPastEnd()
    {
        var path = Path("append-after-eof.bin");
        File.WriteAllBytes(path, [10]);

        Assert.Equal(
            "[10,-1,20,-1]\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', read, R, [type(binary), eof_action(reset)]),
                  get_byte(R, A), get_byte(R, FirstEnd),
                  open('{path}', append, W, [type(binary)]), put_byte(W, 20), close(W),
                  get_byte(R, B), get_byte(R, SecondEnd),
                close(R), write([A,FirstEnd,B,SecondEnd]), nl
                """
            )
        );
    }

    [Fact]
    public async Task NonSeekableBinaryInputPeeksWithoutSeeking()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "FIFOs are Unix inputs.");
        var path = Path("peek.fifo");
        await CreateFifo(path);
        var writer = WriteFifo(path, [10, 20]);

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', read, In, [type(binary)]),
                  stream_property(In, reposition(false)),
                  \+ stream_property(In, position(_)),
                  peek_byte(In, 10), get_byte(In, 10),
                  peek_byte(In, 20), get_byte(In, 20),
                  peek_byte(In, -1), at_end_of_stream(In),
                close(In), write(yes), nl
                """
            )
        );
        await writer.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExplicitRepositionRejectsNonSeekableBinaryInput()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "FIFOs are Unix inputs.");
        var path = Path("reposition.fifo");
        await CreateFifo(path);
        var writer = Task.Run(
            () =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            $"permission_error(open,source_sink,'{path}')",
            PrologTestHost.RunGoal($"catch(open('{path}', read, _, [type(binary), reposition(true)]), error(E, _), writeq(E))")
        );
        await writer.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void OneArgumentByteOperationsUseBinaryCurrentStreams()
    {
        var path = Path("current.bin");

        Assert.Equal(
            "65-66\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, Out, [type(binary)]),
                  set_output(Out), put_byte(65), put_byte(66), set_output(user_output),
                close(Out),
                open('{path}', read, In, [type(binary)]),
                  set_input(In), get_byte(A), get_byte(B), set_input(user_input),
                close(In),
                write(A-B), nl
                """
            )
        );
    }

    [Fact]
    public void OneArgumentTextOutputRejectsABinaryCurrentStream()
    {
        var path = Path("current-text-error.bin");

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, Out, [type(binary)]), set_output(Out),
                  catch(write(x), error(permission_error(output, binary_stream, _), _), true),
                set_output(user_output), close(Out), write(yes), nl
                """
            )
        );
    }

    [Fact]
    public void ReportsBinaryPropertiesAndEndStates()
    {
        var path = Path("properties.bin");

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, Out, [type(binary)]), put_byte(Out, 1), close(Out),
                open('{path}', read, In, [type(binary), alias(bytes)]),
                  stream_property(In, type(binary)),
                  stream_property(In, mode(read)),
                  stream_property(In, input),
                  stream_property(In, reposition(true)),
                  stream_property(In, end_of_stream(not)),
                  get_byte(In, 1),
                  stream_property(In, end_of_stream(at)),
                  peek_byte(In, -1),
                  stream_property(In, end_of_stream(at)),
                  get_byte(In, -1),
                  stream_property(In, end_of_stream(past)),
                close(In),
                write(yes), nl
                """
            )
        );
    }

    [Fact]
    public void OpenTypeUsesTheRightmostOption()
    {
        var path = Path("rightmost.bin");

        Assert.Equal(
            "binary\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, Out, [type(text), type(binary)]),
                  stream_property(Out, type(Type)),
                close(Out),
                write(Type), nl
                """
            )
        );
    }

    [Theory]
    [InlineData("write(binary_out, x)", "permission_error(output,binary_stream,binary_out)")]
    [InlineData("put_code(binary_out, 65)", "permission_error(output,binary_stream,binary_out)")]
    public void TextOutputRejectsBinaryStreams(string operation, string expected)
    {
        var path = Path("wrong-output.bin");
        var goal =
            $"open('{path}', write, _, [type(binary), alias(binary_out)]), "
            + $"catch({operation}, error(E, _), write(E)), close(binary_out)";

        Assert.Equal(expected, PrologTestHost.RunGoal(goal));
    }

    [Theory]
    [InlineData("read(binary_in, _)", "permission_error(input,binary_stream,binary_in)")]
    [InlineData("get_code(binary_in, _)", "permission_error(input,binary_stream,binary_in)")]
    [InlineData("peek_char(binary_in, _)", "permission_error(input,binary_stream,binary_in)")]
    public void TextInputRejectsBinaryStreams(string operation, string expected)
    {
        var path = Path("wrong-input.bin");
        File.WriteAllBytes(path, [65]);
        var goal =
            $"open('{path}', read, _, [type(binary), alias(binary_in)]), "
            + $"catch({operation}, error(E, _), write(E)), close(binary_in)";

        Assert.Equal(expected, PrologTestHost.RunGoal(goal));
    }

    [Theory]
    [InlineData("get_byte(user_input, _)", "permission_error(input,text_stream,user_input)")]
    [InlineData("peek_byte(user_input, _)", "permission_error(input,text_stream,user_input)")]
    [InlineData("put_byte(user_output, 0)", "permission_error(output,text_stream,user_output)")]
    public void ByteOperationsRejectTextStreams(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    [Theory]
    [InlineData("get_byte(bytes, atom)", "type_error(in_byte,atom)")]
    [InlineData("get_byte(bytes, -2)", "type_error(in_byte,-2)")]
    [InlineData("get_byte(bytes, 256)", "type_error(in_byte,256)")]
    public void ReportsInputByteErrors(string operation, string expected)
    {
        var path = Path("input-errors.bin");
        File.WriteAllBytes(path, [0]);
        var goal =
            $"open('{path}', read, _, [type(binary), alias(bytes)]), "
            + $"catch({operation}, error(E, _), write(E)), close(bytes)";

        Assert.Equal(expected, PrologTestHost.RunGoal(goal));
    }

    [Theory]
    [InlineData("put_byte(bytes, _)", "instantiation_error")]
    [InlineData("put_byte(bytes, atom)", "type_error(byte,atom)")]
    [InlineData("put_byte(bytes, -1)", "type_error(byte,-1)")]
    [InlineData("put_byte(bytes, 256)", "type_error(byte,256)")]
    public void ReportsOutputByteErrors(string operation, string expected)
    {
        var path = Path("output-errors.bin");
        var goal =
            $"open('{path}', write, _, [type(binary), alias(bytes)]), "
            + $"catch({operation}, error(E, _), write(E)), close(bytes)";

        Assert.Equal(expected, PrologTestHost.RunGoal(goal));
    }

    [Theory]
    [InlineData("get_byte(_, 256)", "instantiation_error")]
    [InlineData("get_byte(no_such_stream, 256)", "type_error(in_byte,256)")]
    [InlineData("put_byte(user_output, _)", "instantiation_error")]
    [InlineData("put_byte(no_such_stream, 256)", "type_error(byte,256)")]
    public void ReportsIsoByteIoErrorPriority(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    [Theory]
    [InlineData("type(other)", "domain_error(stream_option,type(other))")]
    [InlineData("type(1)", "domain_error(stream_option,type(1))")]
    [InlineData("unknown(value)", "domain_error(stream_option,unknown(value))")]
    [InlineData("type(_)", "instantiation_error")]
    public void ReportsBinaryOpenOptionErrors(string option, string expected)
    {
        var path = Path("option-errors.bin");
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch(open('{path}', write, _, [{option}]), error(E, _), write(E))"));
    }

    private static async Task CreateFifo(string path)
    {
        var start = new ProcessStartInfo("mkfifo");
        start.ArgumentList.Add(path);
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, process.ExitCode);
    }

    private static Task WriteFifo(string path, byte[] bytes) =>
        Task.Run(
            () =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(bytes, 0, bytes.Length);
            },
            TestContext.Current.CancellationToken
        );
}
