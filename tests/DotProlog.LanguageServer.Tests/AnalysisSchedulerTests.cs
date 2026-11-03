using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json.Nodes;
using DotProlog.Runtime;

namespace DotProlog.LanguageServer.Tests;

public sealed class AnalysisSchedulerTests
{
    [Fact]
    public async Task BurstPublishesOnlyLatestVersion()
    {
        ConcurrentQueue<DiagnosticPublication> published = new();
        await using var scheduler = new AnalysisScheduler(
            PrologLanguageMode.Modern,
            null,
            null,
            (items, _, _) =>
            {
                foreach (var item in items)
                    published.Enqueue(item);
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(150)
        );
        for (var version = 1; version <= 20; version++)
            await scheduler.UpdateAsync(
                new DocumentSnapshot("untitled:burst", version == 20 ? "ok." : "value(X).", version),
                version > 1
            );
        await scheduler.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var result = Assert.Single(published);
        Assert.Equal(20, result.Version);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task PendingOutlineUsesLatestAcceptedEditAndCancellationIsPerRequest()
    {
        await using var scheduler = new AnalysisScheduler(
            PrologLanguageMode.Modern,
            null,
            null,
            (_, _, _) => Task.CompletedTask,
            TimeSpan.FromMilliseconds(150)
        );
        var current = new TaskCompletionSource<JsonArray>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await scheduler.UpdateAsync(new DocumentSnapshot("untitled:outline", "old.", 1), false);
        await scheduler.RequestAsync(
            new PendingAnalysisRequest(
                "current",
                workspace => workspace.Symbols("untitled:outline", true),
                (symbols, error) =>
                {
                    Assert.Null(error);
                    current.SetResult(symbols!.AsArray());
                    return Task.CompletedTask;
                }
            )
        );
        await scheduler.RequestAsync(
            new PendingAnalysisRequest(
                "cancelled",
                workspace => workspace.Symbols("untitled:outline", true),
                (_, error) =>
                {
                    cancelled.SetResult(error);
                    return Task.CompletedTask;
                }
            )
        );
        await scheduler.CancelRequestAsync("cancelled");
        Assert.Equal(-32800, await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await scheduler.UpdateAsync(new DocumentSnapshot("untitled:outline", "current.", 2), true);
        var result = await current.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("current/0", Assert.Single(result)!["name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("hover")]
    [InlineData("completion")]
    [InlineData("definition")]
    [InlineData("references")]
    [InlineData("prepareRename")]
    [InlineData("rename")]
    public async Task PendingPositionRequestIsInvalidatedByAnEditInsteadOfUsingShiftedPosition(string requestKind)
    {
        await using var scheduler = new AnalysisScheduler(
            PrologLanguageMode.Modern,
            null,
            null,
            (_, _, _) => Task.CompletedTask,
            TimeSpan.FromMilliseconds(150)
        );
        var response = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await scheduler.UpdateAsync(
            new DocumentSnapshot(
                "untitled:hover",
                requestKind is "definition" or "references" or "prepareRename" or "rename"
                    ? "run :- first. first."
                    : "run :- write(ok).",
                1
            ),
            false
        );
        await scheduler.RequestAsync(
            new PendingAnalysisRequest(
                "hover",
                workspace =>
                    requestKind switch
                    {
                        "prepareRename" => workspace.PrepareRename("untitled:hover", 0, 7),
                        "rename" => workspace.Rename("untitled:hover", 0, 7, "renamed"),
                        "references" => workspace.References("untitled:hover", 0, 7, false),
                        "definition" => workspace.Definition("untitled:hover", 0, 7, false),
                        "completion" => workspace.Completion("untitled:hover", 0, 9),
                        _ => workspace.Hover("untitled:hover", 0, 7, false),
                    },
                (result, error) =>
                {
                    Assert.Null(result);
                    response.SetResult(error);
                    return Task.CompletedTask;
                },
                InvalidateOnChange: true
            )
        );
        await scheduler.UpdateAsync(
            new DocumentSnapshot(
                "untitled:hover",
                requestKind is "definition" or "references" or "prepareRename" or "rename"
                    ? "run :- second. second."
                    : "run :- var(X).",
                2
            ),
            true
        );
        Assert.Equal(-32801, await response.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await scheduler.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var retry = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await scheduler.RequestAsync(
            new PendingAnalysisRequest(
                "retry",
                workspace =>
                    requestKind switch
                    {
                        "prepareRename" => workspace.PrepareRename("untitled:hover", 0, 7),
                        "rename" => workspace.Rename("untitled:hover", 0, 7, "renamed"),
                        "references" => workspace.References("untitled:hover", 0, 7, false),
                        "definition" => workspace.Definition("untitled:hover", 0, 7, false),
                        "completion" => workspace.Completion("untitled:hover", 0, 9),
                        _ => workspace.Hover("untitled:hover", 0, 7, false),
                    },
                (result, error) =>
                {
                    Assert.Null(error);
                    retry.SetResult(result);
                    return Task.CompletedTask;
                },
                InvalidateOnChange: true
            )
        );
        var current = await retry.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        if (requestKind == "prepareRename")
            Assert.Equal("second", current!["placeholder"]!.GetValue<string>());
        else if (requestKind == "rename")
            Assert.Equal(2, Assert.Single(current!["documentChanges"]!.AsArray())!["textDocument"]!["version"]!.GetValue<int>());
        else if (requestKind == "references")
            Assert.Equal(7, Assert.Single((JsonArray)current!)!["range"]!["start"]!["character"]!.GetValue<int>());
        else if (requestKind == "definition")
            Assert.Equal(15, Assert.Single((JsonArray)current!)!["range"]!["start"]!["character"]!.GetValue<int>());
        else if (requestKind == "completion")
            Assert.Contains((JsonArray)current!, item => item!["label"]!.GetValue<string>() == "var/1");
        else
            Assert.StartsWith("var/1", current!["contents"]!["value"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CloseDiscardsPendingEditAndClearsPreviouslyPublishedDiagnostics()
    {
        ConcurrentQueue<DiagnosticPublication> published = new();
        await using var scheduler = new AnalysisScheduler(
            PrologLanguageMode.Modern,
            null,
            null,
            (items, _, _) =>
            {
                foreach (var item in items)
                    published.Enqueue(item);
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(150)
        );
        await scheduler.UpdateAsync(new DocumentSnapshot("untitled:closed", "value(X).", 1), false);
        await scheduler.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotEmpty(Assert.Single(published).Diagnostics);
        await scheduler.UpdateAsync(new DocumentSnapshot("untitled:closed", "broken(", 2), true);
        await scheduler.CloseAsync("untitled:closed");
        await scheduler.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(2, published.Count);
        var cleared = published.Last();
        Assert.Null(cleared.Version);
        Assert.Empty(cleared.Diagnostics);
    }

    [Fact]
    public async Task SupersededReloadCannotCommitEvenWhenItIgnoresCancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var project = new ProjectConfiguration(
            Path.Combine(Path.GetTempPath(), "scheduler.dplproj"),
            PrologLanguageMode.Modern,
            PrologFlagOverrides.None,
            [],
            []
        );
        var calls = 0;
        ConcurrentQueue<string> logs = new();
        ConcurrentQueue<DiagnosticPublication> publications = new();
        await using var scheduler = new AnalysisScheduler(
            PrologLanguageMode.Modern,
            project,
            async _ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.SetResult();
                    await release.Task;
                    throw new InvalidDataException("obsolete failure");
                }
                return project;
            },
            (items, message, _) =>
            {
                if (message is not null)
                    logs.Enqueue(message);
                foreach (var item in items)
                    publications.Enqueue(item);
                return Task.CompletedTask;
            },
            TimeSpan.Zero
        );
        await scheduler.DiskChangedAsync([new Uri(project.ProjectPath).AbsoluteUri]);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await scheduler.UpdateAsync(new DocumentSnapshot("untitled:new", "ok.", 1), false);
        }
        finally
        {
            release.TrySetResult();
        }
        await scheduler.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(2, calls); // The cancelled generation's disk event was retained for retry.
        Assert.Empty(logs);
        Assert.Empty(Assert.Single(publications).Diagnostics);
    }

    [Theory]
    [InlineData("documentSymbol")]
    [InlineData("hover")]
    [InlineData("completion")]
    [InlineData("definition")]
    [InlineData("references")]
    [InlineData("prepareRename")]
    [InlineData("rename")]
    public async Task RequestsAndShutdownRemainResponsiveDuringReevaluation(string requestMethod)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var project = new ProjectConfiguration(
            Path.Combine(Path.GetTempPath(), "blocked.dplproj"),
            PrologLanguageMode.Modern,
            PrologFlagOverrides.None,
            [],
            []
        );
        var server = new LanguageServer(
            PrologLanguageMode.Modern,
            project,
            async token =>
            {
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult();
                    throw;
                }
                return project;
            },
            TimeSpan.Zero
        );
        var incoming = new Pipe();
        var outgoing = new Pipe();
        using var input = incoming.Reader.AsStream();
        using var output = outgoing.Writer.AsStream();
        using var clientWrite = incoming.Writer.AsStream();
        using var clientRead = outgoing.Reader.AsStream();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        Task exchange = ExchangeAsync();
        try
        {
            Assert.Equal(0, await server.RunAsync(input, output, timeout.Token));
        }
        finally
        {
            await timeout.CancelAsync();
            await exchange;
        }

        async Task ExchangeAsync()
        {
            await SendAsync(
                clientWrite,
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{"workspace":{"workspaceEdit":{"documentChanges":true}},"textDocument":{"rename":{"prepareSupport":true}}}}}""",
                timeout.Token
            );
            Assert.Equal(1, (await ReadAsync(clientRead, timeout.Token))["id"]!.GetValue<int>());
            var changed = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "workspace/didChangeWatchedFiles",
                ["params"] = new JsonObject
                {
                    ["changes"] = new JsonArray(
                        new JsonObject { ["uri"] = new Uri(project.ProjectPath).AbsoluteUri, ["type"] = 2 }
                    ),
                },
            };
            await SendAsync(clientWrite, changed.ToJsonString(), timeout.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2), timeout.Token);
            bool positional = requestMethod != "documentSymbol";
            string outline = positional
                ? """{"jsonrpc":"2.0","id":40,"method":"textDocument/hover","params":{"textDocument":{"uri":"untitled:pending"},"position":{"line":0,"character":0}}}"""
                : """{"jsonrpc":"2.0","id":40,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"untitled:pending"}}}""";
            outline = outline.Replace("textDocument/hover", "textDocument/" + requestMethod, StringComparison.Ordinal);
            if (requestMethod == "references")
            {
                var referenceRequest = JsonNode.Parse(outline)!;
                referenceRequest["params"]!["context"] = new JsonObject { ["includeDeclaration"] = false };
                outline = referenceRequest.ToJsonString();
            }
            if (requestMethod == "rename")
            {
                var renameRequest = JsonNode.Parse(outline)!;
                renameRequest["params"]!["newName"] = "renamed";
                outline = renameRequest.ToJsonString();
            }
            await SendAsync(clientWrite, outline, timeout.Token);
            if (positional)
            {
                await SendAsync(
                    clientWrite,
                    """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"untitled:pending","version":1,"text":"run :- write(ok)."}}}""",
                    timeout.Token
                );
                var modified = await ReadAsync(clientRead, timeout.Token);
                Assert.Equal(40, modified["id"]!.GetValue<int>());
                Assert.Equal(-32801, modified["error"]!["code"]!.GetValue<int>());
                await SendAsync(clientWrite, outline, timeout.Token);
            }
            await SendAsync(clientWrite, """{"jsonrpc":"2.0","id":2,"method":"unknown"}""", timeout.Token);
            Assert.Equal(
                2,
                (await ReadAsync(clientRead, timeout.Token).WaitAsync(TimeSpan.FromSeconds(2), timeout.Token))[
                    "id"
                ]!.GetValue<int>()
            );
            await SendAsync(clientWrite, """{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":40}}""", timeout.Token);
            var cancelledReply = await ReadAsync(clientRead, timeout.Token);
            Assert.Equal(40, cancelledReply["id"]!.GetValue<int>());
            Assert.Equal(-32800, cancelledReply["error"]!["code"]!.GetValue<int>());
            await SendAsync(clientWrite, outline, timeout.Token);
            await SendAsync(clientWrite, """{"jsonrpc":"2.0","id":3,"method":"shutdown"}""", timeout.Token);
            var shutdownCancellation = await ReadAsync(clientRead, timeout.Token);
            Assert.Equal(40, shutdownCancellation["id"]!.GetValue<int>());
            Assert.Equal(-32800, shutdownCancellation["error"]!["code"]!.GetValue<int>());
            Assert.Equal(
                3,
                (await ReadAsync(clientRead, timeout.Token).WaitAsync(TimeSpan.FromSeconds(2), timeout.Token))[
                    "id"
                ]!.GetValue<int>()
            );
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2), timeout.Token);
            await SendAsync(clientWrite, """{"jsonrpc":"2.0","method":"exit"}""", timeout.Token);
        }
    }

    private static async Task SendAsync(Stream stream, string text, CancellationToken token)
    {
        byte[] body = Encoding.UTF8.GetBytes(text);
        byte[] header = Encoding.ASCII.GetBytes(FormattableString.Invariant($"Content-Length: {body.Length}\r\n\r\n"));
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(body, token);
        await stream.FlushAsync(token);
    }

    private static async Task<JsonObject> ReadAsync(Stream stream, CancellationToken token)
    {
        List<byte> header = [];
        byte[] one = new byte[1];
        while (true)
        {
            await stream.ReadExactlyAsync(one, token);
            header.Add(one[0]);
            if (header.Count >= 4 && header.TakeLast(4).SequenceEqual("\r\n\r\n"u8.ToArray()))
                break;
        }
        string length = Encoding
            .ASCII.GetString([.. header])
            .Split("\r\n")
            .Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            .Split(':')[1];
        byte[] body = new byte[int.Parse(length, System.Globalization.CultureInfo.InvariantCulture)];
        await stream.ReadExactlyAsync(body, token);
        return JsonNode.Parse(body)!.AsObject();
    }
}
