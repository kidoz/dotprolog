using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using DotProlog.Runtime;

namespace DotProlog.LanguageServer.Tests;

public sealed class ProtocolTests
{
    private const string Initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{}}}""";
    private const string Shutdown = """{"jsonrpc":"2.0","id":2,"method":"shutdown"}""";
    private const string Exit = """{"jsonrpc":"2.0","method":"exit"}""";
    private const string DocumentUri = "untitled:example.pl";

    [Fact]
    public async Task FramedResponsesAreFlushedBeforeExitReturns()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            (int code, List<JsonObject> messages) = await RunAsync(Initialize, Open("value(X)."), Shutdown, Exit);
            Assert.Equal(0, code);
            Assert.Equal(3, messages.Count);
            Assert.Equal(2, messages[^1]["id"]!.GetValue<int>());
        }
    }

    [Fact]
    public async Task InitializesAndShutsDownThroughFramedProtocol()
    {
        (int code, List<JsonObject> messages) = await RunAsync(Initialize, Shutdown, Exit);
        Assert.Equal(0, code);
        Assert.Equal(2, messages.Count);
        Assert.Equal("utf-16", messages[0]["result"]!["capabilities"]!["positionEncoding"]!.GetValue<string>());
        Assert.Equal(1, messages[0]["result"]!["capabilities"]!["textDocumentSync"]!["change"]!.GetValue<int>());
        Assert.True(messages[1].ContainsKey("result"));
        Assert.Null(messages[1]["result"]);
    }

    [Fact]
    public async Task PublishesUnsavedDiagnosticsAndClearsOnChangeAndClose()
    {
        (int code, List<JsonObject> messages) = await RunAsync(
            Initialize,
            Open("value(X)."),
            Change(2, "value(ok)."),
            """{"jsonrpc":"2.0","method":"textDocument/didClose","params":{"textDocument":{"uri":"untitled:example.pl"}}}""",
            Shutdown,
            Exit
        );
        Assert.Equal(0, code);
        Assert.Equal("DPL3001", messages[1]["params"]!["diagnostics"]![0]!["code"]!.GetValue<string>());
        Assert.Equal(1, messages[1]["params"]!["version"]!.GetValue<int>());
        Assert.Empty(messages[2]["params"]!["diagnostics"]!.AsArray());
        Assert.Equal(2, messages[2]["params"]!["version"]!.GetValue<int>());
        Assert.Empty(messages[3]["params"]!["diagnostics"]!.AsArray());
        Assert.Null(messages[3]["params"]!["version"]);
    }

    [Fact]
    public async Task IgnoresOldVersionsAndUnopenedChanges()
    {
        (_, List<JsonObject> messages) = await RunAsync(
            Initialize,
            Change(1, "old(X)."),
            Open("ok."),
            Change(2, "ok."),
            Change(1, "old(X)."),
            Change(2, "old(X)."),
            Shutdown,
            Exit
        );
        Assert.Equal(4, messages.Count);
        Assert.Empty(messages[2]["params"]!["diagnostics"]!.AsArray());
    }

    [Fact]
    public async Task InvalidNotificationsDoNotBreakSubsequentEdits()
    {
        (_, List<JsonObject> messages) = await RunAsync(
            Initialize,
            """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{}}""",
            Open("broken(X"),
            Change(2, "ok."),
            Shutdown,
            Exit
        );
        Assert.Contains(
            messages[1]["params"]!["diagnostics"]!.AsArray(),
            diagnostic => diagnostic!["severity"]!.GetValue<int>() == 1
        );
        Assert.Empty(messages[2]["params"]!["diagnostics"]!.AsArray());
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public async Task DiagnosticRangesUseUtf16Offsets(string newline)
    {
        (_, List<JsonObject> messages) = await RunAsync(Initialize, Open("ok." + newline + "value('😀', X)."), Shutdown, Exit);
        JsonNode diagnostic = messages[1]["params"]!["diagnostics"]!
            .AsArray()
            .Single(item => item!["code"]!.GetValue<string>() == "DPL3001")!;
        Assert.Equal(1, diagnostic["range"]!["start"]!["line"]!.GetValue<int>());
        Assert.Equal(12, diagnostic["range"]!["start"]!["character"]!.GetValue<int>());
        Assert.Equal(13, diagnostic["range"]!["end"]!["character"]!.GetValue<int>());
    }

    [Fact]
    public async Task EnforcesLifecycleAndPreservesStringRequestIds()
    {
        (_, List<JsonObject> messages) = await RunAsync(
            """{"jsonrpc":"2.0","id":"before","method":"hover"}""",
            Initialize,
            Initialize,
            """{"jsonrpc":"2.0","id":"missing","method":"unknown"}""",
            Shutdown,
            """{"jsonrpc":"2.0","id":3,"method":"unknown"}""",
            Exit
        );
        Assert.Equal(-32002, messages[0]["error"]!["code"]!.GetValue<int>());
        Assert.Equal("before", messages[0]["id"]!.GetValue<string>());
        Assert.Equal(-32600, messages[2]["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32601, messages[3]["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32600, messages[5]["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task RecoversAfterMalformedJsonAndInvalidEnvelope()
    {
        (_, List<JsonObject> messages) = await RunAsync("{", "[]", Initialize, Shutdown, Exit);
        Assert.Equal(-32700, messages[0]["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32600, messages[1]["error"]!["code"]!.GetValue<int>());
        Assert.Equal(4, messages.Count);
    }

    [Fact]
    public async Task RejectsInvalidInitializeParametersWithoutChangingState()
    {
        (_, List<JsonObject> messages) = await RunAsync(
            """{"jsonrpc":"2.0","id":9,"method":"initialize","params":{}}""",
            Initialize,
            Shutdown,
            Exit
        );
        Assert.Equal(-32602, messages[0]["error"]!["code"]!.GetValue<int>());
        Assert.NotNull(messages[1]["result"]);
    }

    [Fact]
    public async Task RejectsTrailingJsonAndStillAcceptsNextFrame()
    {
        (_, List<JsonObject> messages) = await RunAsync(Initialize + " {}", Initialize, Shutdown, Exit);
        Assert.Equal(-32700, messages[0]["error"]!["code"]!.GetValue<int>());
        Assert.NotNull(messages[1]["result"]);
    }

    [Fact]
    public async Task ExitBeforeShutdownIsUnsuccessful()
    {
        (int code, _) = await RunAsync(Exit);
        Assert.Equal(1, code);
    }

    [Fact]
    public async Task DoesNotExecuteInitializationOrIncludeSource()
    {
        (_, List<JsonObject> messages) = await RunAsync(
            Initialize,
            Open(":- initialization(halt(42)).\n:- include('nonexistent-lsp-test.pl').\nvalue(X)."),
            Shutdown,
            Exit
        );
        Assert.Equal("DPL3001", messages[1]["params"]!["diagnostics"]![0]!["code"]!.GetValue<string>());
        Assert.Equal(3, messages.Count);
    }

    [Fact]
    public void ModesArePassedToTheReader()
    {
        JsonArray modern = DocumentDiagnostics.Analyze(
            "value(`hello`).",
            DocumentUri,
            PrologLanguageMode.Modern,
            TestContext.Current.CancellationToken
        );
        JsonArray strict = DocumentDiagnostics.Analyze(
            "value(`hello`).",
            DocumentUri,
            PrologLanguageMode.StrictIso,
            TestContext.Current.CancellationToken
        );
        Assert.DoesNotContain(modern, item => item!["severity"]!.GetValue<int>() == 1);
        Assert.Contains(strict, item => item!["severity"]!.GetValue<int>() == 1);
    }

    [Fact]
    public async Task RegistersFileWatchingOnceAfterInitializedAndAcceptsResponse()
    {
        const string supportsWatching =
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{"workspace":{"didChangeWatchedFiles":{"dynamicRegistration":true}}}}}""";
        const string initialized = """{"jsonrpc":"2.0","method":"initialized","params":{}}""";
        (_, List<JsonObject> messages) = await RunAsync(
            supportsWatching,
            initialized,
            """{"jsonrpc":"2.0","id":"dotprolog/watch-files","result":null}""",
            initialized,
            Shutdown,
            Exit
        );
        Assert.Equal(3, messages.Count);
        Assert.NotNull(messages[0]["result"]);
        Assert.Equal("client/registerCapability", messages[1]["method"]!.GetValue<string>());
        JsonNode registration = messages[1]["params"]!["registrations"]![0]!;
        Assert.Equal("workspace/didChangeWatchedFiles", registration["method"]!.GetValue<string>());
        Assert.Equal("**/*", registration["registerOptions"]!["watchers"]![0]!["globPattern"]!.GetValue<string>());
        Assert.Equal(7, registration["registerOptions"]!["watchers"]![0]!["kind"]!.GetValue<int>());
        Assert.Equal(2, messages[2]["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task WatchingIsNotRequestedWithoutClientSupport()
    {
        (_, List<JsonObject> messages) = await RunAsync(
            Initialize,
            """{"jsonrpc":"2.0","method":"initialized","params":{}}""",
            Shutdown,
            Exit
        );
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public async Task WatchRegistrationRejectionLogsOnceAndEditingContinues()
    {
        const string supportsWatching =
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{"workspace":{"didChangeWatchedFiles":{"dynamicRegistration":true}}}}}""";
        const string rejected =
            """{"jsonrpc":"2.0","id":"dotprolog/watch-files","error":{"code":-32601,"message":"unsupported"}}""";
        (_, List<JsonObject> messages) = await RunAsync(
            supportsWatching,
            """{"jsonrpc":"2.0","method":"initialized","params":{}}""",
            rejected,
            rejected,
            Open("value(X)."),
            Shutdown,
            Exit
        );
        Assert.Equal(5, messages.Count);
        Assert.Equal("window/logMessage", messages[2]["method"]!.GetValue<string>());
        Assert.Equal("textDocument/publishDiagnostics", messages[3]["method"]!.GetValue<string>());
    }

    [Fact]
    public async Task InvalidWatchNotificationsAndUnknownResponsesDoNotDisruptEditing()
    {
        (_, List<JsonObject> messages) = await RunAsync(
            Initialize,
            """{"jsonrpc":"2.0","method":"workspace/didChangeWatchedFiles","params":{"changes":[{"uri":"file:///tmp/test.pl","type":4}]}}""",
            """{"jsonrpc":"2.0","method":"workspace/didChangeWatchedFiles","params":{"changes":[{"uri":"not a URI","type":1}]}}""",
            """{"jsonrpc":"2.0","id":"unknown","result":null}""",
            Open("value(X)."),
            Shutdown,
            Exit
        );
        Assert.Equal(3, messages.Count);
        Assert.Equal("textDocument/publishDiagnostics", messages[1]["method"]!.GetValue<string>());
    }

    private static string Open(string text) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "textDocument/didOpen",
            ["params"] = new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = DocumentUri,
                    ["languageId"] = "prolog",
                    ["version"] = 1,
                    ["text"] = text,
                },
            },
        }.ToJsonString();

    private static string Change(int version, string text) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "textDocument/didChange",
            ["params"] = new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = DocumentUri, ["version"] = version },
                ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text }),
            },
        }.ToJsonString();

    // Construct and decode wire frames independently of the production formatter.
    private static Task<(int Code, List<JsonObject> Messages)> RunAsync(params string[] bodies) =>
        RunServerAsync(new LanguageServer(debounce: TimeSpan.Zero), bodies);

    internal static async Task<(int Code, List<JsonObject> Messages)> RunServerAsync(
        LanguageServer server,
        params string[] bodies
    )
    {
        List<byte[]> frames = [];
        foreach (string body in bodies)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            byte[] header = Encoding.ASCII.GetBytes(
                $"Content-Length: {bytes.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n"
            );
            frames.Add([.. header, .. bytes]);
        }
        using var input = new AnalysisInputStream(frames, server.WaitForAnalysisAsync);
        using var output = new MemoryStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int code = await server.RunAsync(input, output, timeout.Token);
        byte[] wire = output.ToArray();
        List<JsonObject> messages = [];
        var position = 0;
        while (position < wire.Length)
        {
            int end = wire.AsSpan(position).IndexOf("\r\n\r\n"u8) + position;
            Assert.True(end >= position);
            string header = Encoding.ASCII.GetString(wire, position, end - position);
            string lengthHeader = header
                .Split("\r\n")
                .Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
            int length = int.Parse(lengthHeader.Split(':')[1], CultureInfo.InvariantCulture);
            position = end + 4;
            messages.Add(JsonNode.Parse(wire.AsSpan(position, length))!.AsObject());
            position += length;
        }
        return (code, messages);
    }
}
