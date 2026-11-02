using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotProlog.Runtime;
using StreamJsonRpc;

namespace DotProlog.LanguageServer;

internal sealed class LanguageServer(
    PrologLanguageMode mode = PrologLanguageMode.Modern,
    ProjectConfiguration? project = null,
    Func<CancellationToken, Task<ProjectConfiguration>>? reloadProject = null,
    TimeSpan? debounce = null
)
{
    private AnalysisScheduler? _analysis;

    internal Task WaitForAnalysisAsync() => _analysis?.WaitForIdleAsync() ?? Task.CompletedTask;

    private readonly FileWatchRegistration _fileWatchRegistration = new();
    private bool _hierarchicalSymbols;
    private bool _markdownHover;
    private bool _initialized;
    private bool _shutdown;

    internal async Task<int> RunAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        // A direct stream writer makes each awaited flush reach stdout before exit. The stream
        // overload uses a background pipe pump whose completion can outlive RunAsync.
        using var transport = new HeaderDelimitedMessageHandler(
            PipeWriter.Create(output, new StreamPipeWriterOptions(leaveOpen: true)),
            PipeReader.Create(input, new StreamPipeReaderOptions(leaveOpen: true)),
            new LspMessageFormatter()
        );
        var analysis = new AnalysisScheduler(
            mode,
            project,
            reloadProject,
            async (publications, log, type) =>
            {
                if (log is not null)
                    await LogAsync(type, log).ConfigureAwait(false);
                foreach (var publication in publications)
                    await PublishAsync(publication.Uri, publication.Version, publication.Diagnostics).ConfigureAwait(false);
            },
            debounce ?? TimeSpan.FromMilliseconds(150)
        );
        await using var analysisLifetime = analysis.ConfigureAwait(false);
        _analysis = analysis;
        while (true)
        {
            LspMessage? message;
            try
            {
                message = (LspMessage?)await transport.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                await SendAsync(Error(null, -32700, "Parse error.")).ConfigureAwait(false);
                continue;
            }
            if (message is null)
                return _shutdown ? 0 : 1;
            // Server-initiated registration has ordinary JSON-RPC responses, not method names.
            // Ignore unknown responses rather than replying and creating an error-response loop.
            if (
                message.Body is JsonObject response
                && !response.ContainsKey("method")
                && TryString(response["jsonrpc"], out var responseVersion)
                && responseVersion == "2.0"
                && ValidId(response["id"])
                && (response.ContainsKey("result") ^ response.ContainsKey("error"))
            )
            {
                JsonObject? log = _fileWatchRegistration.Complete(response);
                if (log is not null && !_shutdown)
                    await SendAsync(log).ConfigureAwait(false);
                continue;
            }
            if (
                message.Body is not JsonObject body
                || !TryString(body["method"], out var method)
                || !TryString(body["jsonrpc"], out var version)
                || version != "2.0"
                || (body.ContainsKey("id") && !ValidId(body["id"]))
            )
            {
                await SendAsync(Error(null, -32600, "Invalid request.")).ConfigureAwait(false);
                continue;
            }
            var request = body.ContainsKey("id");
            JsonNode? id = body["id"];
            if (method == "exit" && !request)
                return _shutdown ? 0 : 1;
            if (_shutdown)
            {
                if (request)
                    await SendAsync(Error(id, -32600, "Server has shut down.")).ConfigureAwait(false);
                continue;
            }
            if (!_initialized && method != "initialize")
            {
                if (request)
                    await SendAsync(Error(id, -32002, "Server not initialized.")).ConfigureAwait(false);
                continue;
            }
            try
            {
                switch (method)
                {
                    case "initialize" when request:
                        if (_initialized)
                        {
                            await SendAsync(Error(id, -32600, "Server already initialized.")).ConfigureAwait(false);
                            break;
                        }
                        var capabilities = RequireObject(RequireObject(body["params"])["capabilities"]);
                        _fileWatchRegistration.Configure(capabilities);
                        _hierarchicalSymbols =
                            capabilities["textDocument"] is JsonObject textCapabilities
                            && textCapabilities["documentSymbol"] is JsonObject symbolCapabilities
                            && symbolCapabilities["hierarchicalDocumentSymbolSupport"] is JsonValue support
                            && support.TryGetValue<bool>(out var supported)
                            && supported;
                        if (
                            capabilities["textDocument"] is JsonObject hoverText
                            && hoverText["hover"] is JsonObject hoverCapabilities
                            && hoverCapabilities["contentFormat"] is JsonArray formats
                        )
                        {
                            foreach (var format in formats)
                            {
                                if (TryString(format, out string kind) && kind is "markdown" or "plaintext")
                                {
                                    _markdownHover = kind == "markdown";
                                    break;
                                }
                            }
                        }
                        _initialized = true;
                        await SendAsync(
                                Result(
                                    id,
                                    new JsonObject
                                    {
                                        ["capabilities"] = new JsonObject
                                        {
                                            ["positionEncoding"] = "utf-16",
                                            ["documentSymbolProvider"] = true,
                                            ["hoverProvider"] = true,
                                            ["textDocumentSync"] = new JsonObject { ["openClose"] = true, ["change"] = 1 },
                                        },
                                        ["serverInfo"] = new JsonObject { ["name"] = "DotProlog" },
                                    }
                                )
                            )
                            .ConfigureAwait(false);
                        break;
                    case "initialized" when !request:
                        await analysis.StartProjectAsync().ConfigureAwait(false);
                        JsonObject? registration = _fileWatchRegistration.CreateRequest();
                        if (registration is not null)
                            await SendAsync(registration).ConfigureAwait(false);
                        break;
                    case "workspace/didChangeWatchedFiles" when !request:
                        var fileChanges =
                            RequireObject(body["params"])["changes"] as JsonArray ?? throw new InvalidParametersException();
                        List<string> changedUris = [];
                        // Validate the entire batch before applying any changes.
                        foreach (JsonNode? change in fileChanges)
                        {
                            JsonObject item = RequireObject(change);
                            string changedUri = RequireString(item["uri"]);
                            DocumentSnapshot.Key(changedUri);
                            if (RequireInteger(item["type"]) is < 1 or > 3)
                                throw new InvalidParametersException();
                            changedUris.Add(changedUri);
                        }
                        await analysis.DiskChangedAsync(changedUris).ConfigureAwait(false);
                        break;
                    case "textDocument/documentSymbol" when request:
                        string symbolUri = RequireString(RequireObject(RequireObject(body["params"])["textDocument"])["uri"]);
                        DocumentSnapshot.Key(symbolUri);
                        await analysis
                            .RequestAsync(
                                new PendingAnalysisRequest(
                                    id!.ToJsonString(),
                                    workspace => workspace.Symbols(symbolUri, _hierarchicalSymbols),
                                    (symbols, error) => ReplyAnalysisAsync(id, symbols, error)
                                )
                            )
                            .ConfigureAwait(false);
                        break;
                    case "textDocument/hover" when request:
                        JsonObject hoverParameters = RequireObject(body["params"]);
                        string hoverUri = RequireString(RequireObject(hoverParameters["textDocument"])["uri"]);
                        DocumentSnapshot.Key(hoverUri);
                        JsonObject position = RequireObject(hoverParameters["position"]);
                        int line = RequireInteger(position["line"]);
                        int character = RequireInteger(position["character"]);
                        if (line < 0 || character < 0)
                            throw new InvalidParametersException();
                        await analysis
                            .RequestAsync(
                                new PendingAnalysisRequest(
                                    id!.ToJsonString(),
                                    workspace => workspace.Hover(hoverUri, line, character, _markdownHover),
                                    (hover, error) => ReplyAnalysisAsync(id, hover, error),
                                    InvalidateOnChange: true
                                )
                            )
                            .ConfigureAwait(false);
                        break;
                    case "$/cancelRequest" when !request:
                        JsonNode? cancelledId = RequireObject(body["params"])["id"];
                        if (!ValidId(cancelledId))
                            throw new InvalidParametersException();
                        await analysis.CancelRequestAsync(cancelledId!.ToJsonString()).ConfigureAwait(false);
                        break;
                    case "shutdown" when request:
                        _shutdown = true;
                        await analysis.StopAsync().ConfigureAwait(false);
                        await SendAsync(Result(id, null)).ConfigureAwait(false);
                        break;
                    case "textDocument/didOpen" when !request:
                    case "textDocument/didChange" when !request:
                    case "textDocument/didClose" when !request:
                        var parameters = RequireObject(body["params"]);
                        var document = RequireObject(parameters["textDocument"]);
                        var uri = RequireString(document["uri"]);
                        if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
                            throw new InvalidParametersException();
                        if (method == "textDocument/didClose")
                        {
                            await analysis.CloseAsync(uri).ConfigureAwait(false);
                            break;
                        }
                        var documentVersion = RequireInteger(document["version"]);
                        string text;
                        if (method == "textDocument/didOpen")
                            text = RequireString(document["text"]);
                        else
                        {
                            var changes = parameters["contentChanges"] as JsonArray ?? throw new InvalidParametersException();
                            if (changes.Count == 0)
                                break;
                            if (changes.Any(change => change is not JsonObject item || item.ContainsKey("range")))
                                throw new InvalidParametersException();
                            text = RequireString(changes[^1]!["text"]);
                        }
                        await analysis
                            .UpdateAsync(new DocumentSnapshot(uri, text, documentVersion), method == "textDocument/didChange")
                            .ConfigureAwait(false);
                        break;
                    default:
                        if (request)
                            await SendAsync(Error(id, -32601, "Method not found.")).ConfigureAwait(false);
                        break;
                }
            }
            catch (InvalidParametersException)
            {
                if (request)
                    await SendAsync(Error(id, -32602, "Invalid parameters.")).ConfigureAwait(false);
            }
        }

        async Task ReplyAnalysisAsync(JsonNode? id, JsonNode? symbols, int? error)
        {
            await SendAsync(
                    error is { } code
                        ? Error(
                            id,
                            code,
                            code == -32801 ? "Content modified; retry the request."
                                : code == -32800 ? "Request cancelled."
                                : code == -32600 ? "Duplicate request id."
                                : "Analysis failed."
                        )
                        : Result(id, symbols)
                )
                .ConfigureAwait(false);
        }

        ValueTask LogAsync(int type, string message) =>
            SendAsync(
                new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "window/logMessage",
                    ["params"] = new JsonObject { ["type"] = type, ["message"] = message },
                }
            );

        ValueTask SendAsync(JsonObject body) => transport.WriteAsync(new LspMessage(body), cancellationToken);

        ValueTask PublishAsync(string uri, int? version, JsonArray diagnostics)
        {
            var parameters = new JsonObject { ["uri"] = uri, ["diagnostics"] = diagnostics };
            if (version is not null)
                parameters["version"] = version.Value;
            return SendAsync(
                new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "textDocument/publishDiagnostics",
                    ["params"] = parameters,
                }
            );
        }
    }

    private static JsonObject RequireObject(JsonNode? node) => node as JsonObject ?? throw new InvalidParametersException();

    private static string RequireString(JsonNode? node) =>
        TryString(node, out var value) ? value : throw new InvalidParametersException();

    private static int RequireInteger(JsonNode? node) =>
        node is JsonValue json && json.TryGetValue<int>(out var value) ? value : throw new InvalidParametersException();

    private static bool TryString(JsonNode? node, out string value)
    {
        value = string.Empty;
        return node is JsonValue json && json.TryGetValue(out value!);
    }

    private static bool ValidId(JsonNode? node) =>
        node is JsonValue value && (value.TryGetValue<string>(out _) || value.TryGetValue<long>(out _));

    private static JsonObject Result(JsonNode? id, JsonNode? result) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result,
        };

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        };
}
