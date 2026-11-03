using System.Text.Json.Nodes;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.LanguageServer.Tests;

public sealed class RenameTests : IDisposable
{
    private const string Uri = "untitled:rename";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rename-" + Guid.NewGuid().ToString("N"));
    private const string Initialize =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{"workspace":{"workspaceEdit":{"documentChanges":true}},"textDocument":{"rename":{"prepareSupport":true}}}}}""";

    public RenameTests() => Directory.CreateDirectory(_folder);

    [Theory]
    [InlineData(PrologLanguageMode.Modern)]
    [InlineData(PrologLanguageMode.StrictIso)]
    public void AppliedRenamePreservesSolutionsThroughStaticMetaCalls(PrologLanguageMode mode)
    {
        const string text = "p(a). p(b). values(X) :- call(p, X).";
        var workspace = new DocumentWorkspace(mode);
        workspace.Update(new DocumentSnapshot(Uri, text, 1));
        var change = Assert.Single(workspace.Rename(Uri, 0, 0, "renamed")["documentChanges"]!.AsArray())!;
        string edited = Apply(text, change["edits"]!.AsArray());
        using var engine = new PrologEngine(mode);
        Assert.True(engine.ConsultText(edited).Success);
        Assert.Equal(["a", "b"], engine.Query("values(X)").Solutions().Select(solution => solution["X"].ToString()).ToArray());
    }

    [Fact]
    public void VersionedUtf16EditsPreserveDataOtherAritiesAndUnrelatedPrograms()
    {
        const string text =
            "'item'(a). item(b). item(_, _).\r\nrun :- write('😀'), 'item'(a), call(item, b), write(item(data)), item(a,b).";
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
        workspace.Update(new DocumentSnapshot(Uri, text, 7));
        workspace.Update(new DocumentSnapshot("untitled:other", "item(x). run :- item(x).", 1));
        var prepared = workspace.PrepareRename(Uri, 0, 1);
        Assert.Equal("item", prepared!["placeholder"]!.GetValue<string>());
        var result = workspace.Rename(Uri, 0, 1, "renamed");
        var change = Assert.Single(result["documentChanges"]!.AsArray())!;
        Assert.Equal(7, change["textDocument"]!["version"]!.GetValue<int>());
        Assert.Equal(Uri, change["textDocument"]!["uri"]!.GetValue<string>());
        Assert.Equal(4, change["edits"]!.AsArray().Count);
        string edited = Apply(text, change["edits"]!.AsArray());
        Assert.Equal(
            "renamed(a). renamed(b). item(_, _).\r\nrun :- write('😀'), renamed(a), call(renamed, b), write(item(data)), item(a,b).",
            edited
        );
        workspace.Update(new DocumentSnapshot(Uri, edited, 8));
        Assert.NotNull(workspace.PrepareRename(Uri, 0, 0));
        Assert.Equal(4, workspace.References(Uri, 0, 0, true).Count);
        Assert.Empty(workspace.Rename(Uri, 0, 0, "renamed")["documentChanges"]!.AsArray());
    }

    [Fact]
    public void ProjectRenameEditsExportsSelectiveReexportsImportsAndUnsavedHeads()
    {
        string library = Source("library.pl", ":- module(library, [item/1]). item(a).");
        string facade = Source("facade.pl", ":- module(facade, []). :- reexport(library, [item/1]).");
        const string clientText = ":- module(client, []). :- use_module('facade.pl', [item/1]). run :- item(a), library:item(b).";
        string client = Source("client.pl", clientText);
        var workspace = Project(client, facade, library);
        const string overlay = ":- module(library, [item/1]). item(a). item(b).";
        workspace.Update(new DocumentSnapshot(library, overlay, 4));
        var result = workspace.Rename(client, 0, clientText.IndexOf("item/1", StringComparison.Ordinal), "entry");
        var changes = result["documentChanges"]!.AsArray();
        Assert.Equal(3, changes.Count);
        Assert.Equal(7, changes.Sum(change => change!["edits"]!.AsArray().Count));
        foreach (var change in changes)
        {
            string uri = change!["textDocument"]!["uri"]!.GetValue<string>();
            if (uri == library)
                Assert.Equal(4, change["textDocument"]!["version"]!.GetValue<int>());
            else
                Assert.Null(change["textDocument"]!["version"]);
            string before = uri == library ? overlay : File.ReadAllText(new System.Uri(uri).LocalPath);
            workspace.Update(new DocumentSnapshot(uri, Apply(before, change["edits"]!.AsArray()), 5));
        }
        Assert.Equal(
            2,
            workspace.References(client, 0, clientText.IndexOf("item(a)", StringComparison.Ordinal) + 2, false).Count
        );
        Assert.Contains("item(a)", File.ReadAllText(new System.Uri(library).LocalPath), StringComparison.Ordinal);
    }

    [Fact]
    public void DcgIndicatorsAndMetaTemplatesAreEdited()
    {
        const string text =
            ":- module(m, [word//1]). :- multifile(word//1). :- discontiguous(word//1). word(X) --> [X]. start --> word(a). run :- phrase(word(a), []), call(word(a), [], []).";
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
        workspace.Update(new DocumentSnapshot(Uri, text, 1));
        var change = Assert.Single(
            workspace.Rename(Uri, 0, text.IndexOf("word//1", StringComparison.Ordinal), "token")["documentChanges"]!.AsArray()
        )!;
        Assert.Equal(7, change["edits"]!.AsArray().Count);
        string edited = Apply(text, change["edits"]!.AsArray());
        Assert.DoesNotContain("word", edited, StringComparison.Ordinal);
        workspace.Update(new DocumentSnapshot(Uri, edited, 2));
        Assert.NotNull(workspace.PrepareRename(Uri, 0, edited.IndexOf("token//1", StringComparison.Ordinal)));
        workspace.Update(new DocumentSnapshot(Uri, ":- meta_predicate(wrapper(?)). wrapper(_). run :- wrapper(data).", 3));
        Assert.Equal(
            3,
            Assert.Single(workspace.Rename(Uri, 0, 18, "wrapped")["documentChanges"]!.AsArray())!["edits"]!.AsArray().Count
        );
    }

    [Theory]
    [InlineData("p. run(G) :- call(G).")]
    [InlineData("p. run :- missing.")]
    [InlineData("p. :- dynamic(p/0).")]
    [InlineData("p. run :- assertz(p).")]
    [InlineData("p. broken(.")]
    [InlineData("p. :- use_module('missing.pl').")]
    public void UnsafeSnapshotsReturnNoPrepareAndRenameError(string text)
    {
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
        workspace.Update(new DocumentSnapshot(Uri, text, 1));
        Assert.Null(workspace.PrepareRename(Uri, 0, 0));
        Assert.Equal(-32803, Assert.Throws<AnalysisRequestException>(() => workspace.Rename(Uri, 0, 0, "renamed")).Code);
    }

    [Fact]
    public void RenameRechecksCurrentSnapshotAfterPrepare()
    {
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
        workspace.Update(new DocumentSnapshot(Uri, "p. q(a). run :- p.", 1));
        Assert.NotNull(workspace.PrepareRename(Uri, 0, 0));
        Assert.NotEmpty(workspace.Rename(Uri, 0, 0, "q")["documentChanges"]!.AsArray());
        workspace.Update(new DocumentSnapshot(Uri, "p. q. run :- p.", 2));
        Assert.Equal(-32803, Assert.Throws<AnalysisRequestException>(() => workspace.Rename(Uri, 0, 0, "q")).Code);
        Assert.Equal(-32602, Assert.Throws<AnalysisRequestException>(() => workspace.Rename(Uri, 0, 0, "write")).Code);
        workspace.RenameConfigurationCurrent = false;
        Assert.Null(workspace.PrepareRename(Uri, 0, 0));
        Assert.Equal(-32803, Assert.Throws<AnalysisRequestException>(() => workspace.Rename(Uri, 0, 0, "renamed")).Code);
    }

    [Fact]
    public void MissingProjectSourcesAndAmbiguousIncludesAreRejected()
    {
        string library = Source("library.pl", "p.");
        string missing = new System.Uri(Path.Combine(_folder, "missing.pl")).AbsoluteUri;
        Assert.Null(Project(library, missing).PrepareRename(library, 0, 0));
        string child = Source("child.pl", "p. run :- p.");
        string root = Source("root.pl", ":- module(a, []). :- include('child.pl'). :- module(b, []). :- include('child.pl').");
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
        workspace.Update(new DocumentSnapshot(root, File.ReadAllText(new System.Uri(root).LocalPath), 1));
        Assert.Null(workspace.PrepareRename(child, 0, 0));
        Assert.Throws<AnalysisRequestException>(() => workspace.Rename(child, 0, 0, "renamed"));
        File.WriteAllText(new System.Uri(child).LocalPath, "run :- p.");
        const string multiple = ":- module(a, []). p. :- include('child.pl'). :- module(b, []). p. :- include('child.pl').";
        workspace.Update(new DocumentSnapshot(root, multiple, 2));
        Assert.Null(workspace.PrepareRename(root, 0, multiple.IndexOf("p.", StringComparison.Ordinal)));
    }

    [Fact]
    public void RepeatedIncludesDeduplicatePhysicalEditsAndHonorUnsavedBuffers()
    {
        string child = Source("child.pl", "p. run :- p.");
        string root = Source("root.pl", ":- include('child.pl'). :- include('child.pl').");
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
        workspace.Update(new DocumentSnapshot(root, File.ReadAllText(new System.Uri(root).LocalPath), 1));
        workspace.Update(new DocumentSnapshot(child, "p. p. run :- p.", 3));
        var change = Assert.Single(workspace.Rename(child, 0, 0, "renamed")["documentChanges"]!.AsArray())!;
        Assert.Equal(3, change["textDocument"]!["version"]!.GetValue<int>());
        Assert.Equal(3, change["edits"]!.AsArray().Count);
        workspace.Close(child);
        change = Assert.Single(workspace.Rename(child, 0, 0, "renamed")["documentChanges"]!.AsArray())!;
        Assert.Null(change["textDocument"]!["version"]);
        Assert.Equal(2, change["edits"]!.AsArray().Count);
    }

    [Fact]
    public async Task QueuedRejectionLeavesOtherRequestsAndAnalysisUsable()
    {
        await using var scheduler = new AnalysisScheduler(
            PrologLanguageMode.Modern,
            null,
            null,
            (_, _, _) => Task.CompletedTask,
            TimeSpan.FromMilliseconds(100)
        );
        var rejected = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await scheduler.UpdateAsync(new DocumentSnapshot(Uri, "p. run :- p.", 1), false);
        await scheduler.RequestAsync(
            new PendingAnalysisRequest(
                "reject",
                workspace => workspace.Rename(Uri, 0, 0, "Invalid"),
                (_, error) =>
                {
                    Assert.Fail($"Expected the detailed rejection, received {error}.");
                    return Task.CompletedTask;
                },
                ReplyError: (code, _) =>
                {
                    rejected.SetResult(code);
                    return Task.CompletedTask;
                }
            )
        );
        await scheduler.RequestAsync(
            new PendingAnalysisRequest(
                "accept",
                workspace => workspace.Rename(Uri, 0, 0, "renamed"),
                (result, error) =>
                {
                    Assert.Null(error);
                    accepted.SetResult(result);
                    return Task.CompletedTask;
                }
            )
        );
        Assert.Equal(-32602, await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.NotNull(await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedProjectReevaluationDisablesRenameUntilRecovery()
    {
        string source = Source("source.pl", "p. run :- p.");
        var configuration = new ProjectConfiguration(
            Path.Combine(_folder, "app.dplproj"),
            PrologLanguageMode.Modern,
            PrologFlagOverrides.None,
            [new System.Uri(source).LocalPath],
            []
        );
        int reloads = 0;
        var server = new LanguageServer(
            PrologLanguageMode.Modern,
            configuration,
            _ =>
                ++reloads == 1
                    ? Task.FromException<ProjectConfiguration>(new InvalidDataException("Broken project"))
                    : Task.FromResult(configuration),
            TimeSpan.Zero
        );
        string changed = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "workspace/didChangeWatchedFiles",
            ["params"] = new JsonObject
            {
                ["changes"] = new JsonArray(
                    new JsonObject { ["uri"] = new System.Uri(configuration.ProjectPath).AbsoluteUri, ["type"] = 2 }
                ),
            },
        }.ToJsonString();
        string RequestSource(int id, string method) =>
            Request(id, method, "renamed").Replace(Uri, source, StringComparison.Ordinal);
        var (_, messages) = await ProtocolTests.RunServerAsync(
            server,
            Initialize,
            """{"jsonrpc":"2.0","method":"initialized","params":{}}""",
            RequestSource(2, "prepareRename"),
            changed,
            RequestSource(3, "prepareRename"),
            RequestSource(4, "rename"),
            changed,
            RequestSource(5, "rename"),
            """{"jsonrpc":"2.0","id":99,"method":"shutdown"}""",
            """{"jsonrpc":"2.0","method":"exit"}"""
        );
        Assert.NotNull(Reply(messages, 2)["result"]);
        Assert.Null(Reply(messages, 3)["result"]);
        Assert.Equal(-32803, Reply(messages, 4)["error"]!["code"]!.GetValue<int>());
        Assert.Contains("reevaluation", Reply(messages, 4)["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.NotNull(Reply(messages, 5)["result"]);
    }

    [Fact]
    public async Task ProtocolNegotiatesPreparationAndFailuresDoNotPoisonLaterRequests()
    {
        var (_, messages) = await ProtocolTests.RunServerAsync(
            new LanguageServer(debounce: TimeSpan.Zero),
            Initialize,
            Open("p. run :- p."),
            Request(2, "prepareRename"),
            Request(3, "rename", "Invalid"),
            Request(4, "rename", "renamed"),
            """{"jsonrpc":"2.0","id":99,"method":"shutdown"}""",
            """{"jsonrpc":"2.0","method":"exit"}"""
        );
        Assert.True(messages[0]["result"]!["capabilities"]!["renameProvider"]!["prepareProvider"]!.GetValue<bool>());
        Assert.Equal("p", Reply(messages, 2)["result"]!["placeholder"]!.GetValue<string>());
        Assert.Equal(-32602, Reply(messages, 3)["error"]!["code"]!.GetValue<int>());
        Assert.NotNull(Reply(messages, 4)["result"]!["documentChanges"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VersionedEditsAreRequiredAndPrepareOptionsAreNegotiated(bool documentChanges)
    {
        var initialize = JsonNode.Parse(Initialize)!;
        initialize["params"]!["capabilities"]!["textDocument"] = new JsonObject();
        initialize["params"]!["capabilities"]!["workspace"]!["workspaceEdit"]!["documentChanges"] = documentChanges;
        var (_, messages) = await ProtocolTests.RunServerAsync(
            new LanguageServer(debounce: TimeSpan.Zero),
            initialize.ToJsonString(),
            Open("p."),
            Request(2, "rename", "renamed"),
            """{"jsonrpc":"2.0","id":99,"method":"shutdown"}""",
            """{"jsonrpc":"2.0","method":"exit"}"""
        );
        Assert.Equal(documentChanges, messages[0]["result"]!["capabilities"]!["renameProvider"]!.GetValue<bool>());
        Assert.Equal(documentChanges, Reply(messages, 2)["error"] is null);
    }

    private static string Apply(string text, JsonArray edits)
    {
        var positions = new SourcePositions(text);
        foreach (var edit in edits.Reverse())
        {
            var start = edit!["range"]!["start"]!;
            var end = edit["range"]!["end"]!;
            int from = positions.Offset(start["line"]!.GetValue<int>(), start["character"]!.GetValue<int>())!.Value;
            int to = positions.Offset(end["line"]!.GetValue<int>(), end["character"]!.GetValue<int>())!.Value;
            text = text[..from] + edit["newText"]!.GetValue<string>() + text[to..];
        }
        return text;
    }

    private string Source(string name, string text)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return new System.Uri(path).AbsoluteUri;
    }

    private DocumentWorkspace Project(params string[] uris)
    {
        var configuration = new ProjectConfiguration(
            Path.Combine(_folder, "app.dplproj"),
            PrologLanguageMode.Modern,
            PrologFlagOverrides.None,
            uris.Select(uri => new System.Uri(uri).LocalPath).ToArray(),
            []
        );
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern, configuration);
        workspace.StartProject();
        return workspace;
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
                    ["uri"] = Uri,
                    ["text"] = text,
                    ["version"] = 1,
                },
            },
        }.ToJsonString();

    private static string Request(int id, string method, string? newName = null) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "textDocument/" + method,
            ["params"] = new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = Uri },
                ["position"] = new JsonObject { ["line"] = 0, ["character"] = 0 },
                ["newName"] = newName,
            },
        }.ToJsonString();

    private static JsonNode Reply(IReadOnlyList<JsonNode> messages, int id) =>
        Assert.Single(
            messages,
            message => message["id"] is JsonValue value && value.TryGetValue<int>(out var number) && number == id
        );

    public void Dispose() => Directory.Delete(_folder, true);
}
