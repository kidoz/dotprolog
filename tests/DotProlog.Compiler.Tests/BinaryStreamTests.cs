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
        string path = Path("bytes.bin");

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
        string path = Path("append.bin");

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
    public void OneArgumentByteOperationsUseBinaryCurrentStreams()
    {
        string path = Path("current.bin");

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
    public void ReportsBinaryPropertiesAndEndStates()
    {
        string path = Path("properties.bin");

        Assert.Equal(
            "yes\n",
            PrologTestHost.RunGoal(
                $"""
                open('{path}', write, Out, [type(binary)]), put_byte(Out, 1), close(Out),
                open('{path}', read, In, [type(binary), alias(bytes)]),
                  stream_property(In, type(binary)),
                  stream_property(In, mode(read)),
                  stream_property(In, input),
                  stream_property(In, reposition(false)),
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
        string path = Path("rightmost.bin");

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
    [InlineData("write(binary_out, x)", "permission_error(output,binary_stream,binary_out/0)")]
    [InlineData("put_code(binary_out, 65)", "permission_error(output,binary_stream,binary_out/0)")]
    public void TextOutputRejectsBinaryStreams(string operation, string expected)
    {
        string path = Path("wrong-output.bin");
        string goal =
            $"open('{path}', write, _, [type(binary), alias(binary_out)]), "
            + $"catch({operation}, error(E, _), write(E)), close(binary_out)";

        Assert.Equal(expected, PrologTestHost.RunGoal(goal));
    }

    [Theory]
    [InlineData("read(binary_in, _)", "permission_error(input,binary_stream,binary_in/0)")]
    [InlineData("get_code(binary_in, _)", "permission_error(input,binary_stream,binary_in/0)")]
    [InlineData("peek_char(binary_in, _)", "permission_error(input,binary_stream,binary_in/0)")]
    public void TextInputRejectsBinaryStreams(string operation, string expected)
    {
        string path = Path("wrong-input.bin");
        File.WriteAllBytes(path, [65]);
        string goal =
            $"open('{path}', read, _, [type(binary), alias(binary_in)]), "
            + $"catch({operation}, error(E, _), write(E)), close(binary_in)";

        Assert.Equal(expected, PrologTestHost.RunGoal(goal));
    }

    [Theory]
    [InlineData("get_byte(user_input, _)", "permission_error(input,text_stream,user_input/0)")]
    [InlineData("peek_byte(user_input, _)", "permission_error(input,text_stream,user_input/0)")]
    [InlineData("put_byte(user_output, 0)", "permission_error(output,text_stream,user_output/0)")]
    public void ByteOperationsRejectTextStreams(string goal, string expected) =>
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch({goal}, error(E, _), write(E))"));

    [Theory]
    [InlineData("get_byte(bytes, atom)", "type_error(in_byte,atom)")]
    [InlineData("get_byte(bytes, -2)", "type_error(in_byte,-2)")]
    [InlineData("get_byte(bytes, 256)", "type_error(in_byte,256)")]
    public void ReportsInputByteErrors(string operation, string expected)
    {
        string path = Path("input-errors.bin");
        File.WriteAllBytes(path, [0]);
        string goal =
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
        string path = Path("output-errors.bin");
        string goal =
            $"open('{path}', write, _, [type(binary), alias(bytes)]), "
            + $"catch({operation}, error(E, _), write(E)), close(bytes)";

        Assert.Equal(expected, PrologTestHost.RunGoal(goal));
    }

    [Theory]
    [InlineData("type(other)", "domain_error(stream_option,type(other))")]
    [InlineData("type(1)", "domain_error(stream_option,type(1))")]
    [InlineData("unknown(value)", "domain_error(stream_option,unknown(value))")]
    [InlineData("type(_)", "instantiation_error")]
    public void ReportsBinaryOpenOptionErrors(string option, string expected)
    {
        string path = Path("option-errors.bin");
        Assert.Equal(expected, PrologTestHost.RunGoal($"catch(open('{path}', write, _, [{option}]), error(E, _), write(E))"));
    }
}
