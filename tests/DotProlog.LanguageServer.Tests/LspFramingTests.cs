using System.Text;
using System.Text.Json.Nodes;

namespace DotProlog.LanguageServer.Tests;

public sealed class LspFramingTests
{
    [Theory]
    [InlineData("Content-Length: nope\r\n\r\n")]
    [InlineData("Content-Length: -1\r\n\r\n")]
    [InlineData("Content-Length: 2147483648\r\n\r\n")]
    [InlineData("Content-Length: 8388609\r\n\r\n")]
    [InlineData("Content-Length: 2\r\nContent-Length: 2\r\n\r\n{}")]
    [InlineData("Content-Type: application/vscode-jsonrpc\r\n\r\n")]
    [InlineData("Content-Length 2\r\n\r\n{}")]
    [InlineData("Content-Length: 2\r\nContent-Type: application/vscode-jsonrpc; charset=utf-16\r\n\r\n{}")]
    [InlineData("Content-Length: 2\r\n")]
    [InlineData("Content-Length: 2\r\n\r\n{")]
    public async Task MalformedFramesEndTheSessionWithAFlushedProtocolError(string wire)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(wire));
        using var output = new MemoryStream();
        var server = new LanguageServer();
        var code = await server.RunAsync(input, output, TestContext.Current.CancellationToken);
        Assert.Equal(1, code);
        var response = Encoding.UTF8.GetString(output.ToArray());
        var body = JsonNode.Parse(response[(response.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..])!;
        Assert.Equal(-32700, body["error"]!["code"]!.GetValue<int>());
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeaderLimitAppliesWithAndWithoutATerminator(bool terminated)
    {
        var header =
            "Content-Length: 2\r\nX-Padding: "
            + new string('a', LspMessageReader.MaximumHeaderBytes)
            + (terminated ? "\r\n\r\n{}" : "");
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(header));
        var reader = new LspMessageReader(input);
        await using var lifetime = reader;
        await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BodyAtTheLimitIsAcceptedAndFollowingFrameIsPreserved()
    {
        var body = "{\"method\":\"ok\",\"padding\":\"";
        body += new string('a', LspMessageReader.MaximumBodyBytes - body.Length - 2) + "\"}";
        var wire = $"Content-Length: {body.Length}\r\n\r\n{body}Content-Length: 2\r\n\r\n{{}}";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(wire));
        var reader = new LspMessageReader(input);
        await using var lifetime = reader;
        Assert.Equal("ok", (await reader.ReadAsync(TestContext.Current.CancellationToken))!.Body!["method"]!.GetValue<string>());
        Assert.Empty((await reader.ReadAsync(TestContext.Current.CancellationToken))!.Body!.AsObject());
        Assert.Null(await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadsFragmentedHeadersAndBodiesWithUtf8ByteLengths()
    {
        const string body = "{\"method\":\"😀\"}";
        var bytes = Encoding.UTF8.GetBytes(body);
        var wire = Encoding.UTF8.GetBytes(
            $"Content-Length: {bytes.Length}\r\nContent-Type: application/vscode-jsonrpc; charset=utf8\r\n\r\n{body}"
        );
        using var input = new FragmentedInput(wire);
        var reader = new LspMessageReader(input);
        await using var lifetime = reader;
        Assert.Equal("😀", (await reader.ReadAsync(TestContext.Current.CancellationToken))!.Body!["method"]!.GetValue<string>());
        Assert.Null(await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    private sealed class FragmentedInput(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }
}
