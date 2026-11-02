using System.Text.Json.Nodes;
using DotProlog.Runtime;

namespace DotProlog.LanguageServer.Tests;

public sealed class CompletionTests
{
    private const string Uri = "untitled:completion";

    [Fact]
    public async Task ProtocolAdvertisesCompletionAndReturnsUtf16ReplacementEdits()
    {
        const string line = "run :- write('😀'), memOther";
        int start = line.IndexOf("memOther", StringComparison.Ordinal);
        var (_, messages) = await ProtocolTests.RunServerAsync(
            new LanguageServer(debounce: TimeSpan.Zero),
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{}}}""",
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "textDocument/didOpen",
                ["params"] = new JsonObject
                {
                    ["textDocument"] = new JsonObject
                    {
                        ["uri"] = Uri,
                        ["version"] = 1,
                        ["text"] = "% comment\r\n" + line,
                    },
                },
            }.ToJsonString(),
            Request(2, 1, start + 3),
            Request(3, -1, 0),
            Request(4, 0, -1),
            Request(5, 99, 0),
            """{"jsonrpc":"2.0","id":99,"method":"shutdown"}""",
            """{"jsonrpc":"2.0","method":"exit"}"""
        );
        Assert.False(messages[0]["result"]!["capabilities"]!["completionProvider"]!["resolveProvider"]!.GetValue<bool>());
        var items = (JsonArray)Reply(messages, 2)["result"]!;
        var member = Assert.Single(items, item => item!["label"]!.GetValue<string>() == "member/2")!;
        Assert.Equal(3, member["kind"]!.GetValue<int>());
        Assert.Equal("member", member["textEdit"]!["newText"]!.GetValue<string>());
        Assert.Equal(start, member["textEdit"]!["range"]!["start"]!["character"]!.GetValue<int>());
        Assert.Equal(start + 8, member["textEdit"]!["range"]!["end"]!["character"]!.GetValue<int>());
        Assert.Equal(1, member["textEdit"]!["range"]!["start"]!["line"]!.GetValue<int>());
        Assert.Equal(-32602, Reply(messages, 3)["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32602, Reply(messages, 4)["error"]!["code"]!.GetValue<int>());
        Assert.Empty((JsonArray)Reply(messages, 5)["result"]!);
    }

    [Fact]
    public void UnsavedEditsScopeVariablesAndRemoveClosedDocuments()
    {
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
        const string text = "before(Other). run(Value, _Hidden, _) :- f(Va";
        workspace.Update(new DocumentSnapshot(Uri, text, 1));
        var variable = Assert.Single(workspace.Completion(Uri, 0, text.Length))!;
        Assert.Equal("Value", variable["label"]!.GetValue<string>());
        Assert.Equal(6, variable["kind"]!.GetValue<int>());
        const string edited = "member(_, _). member(_, _, _). run :- mem";
        workspace.Update(new DocumentSnapshot(Uri, edited, 2));
        var items = workspace.Completion(Uri, 0, edited.Length);
        Assert.Equal(["member/2", "member/3", "memberchk/2"], items.Select(item => item!["label"]!.GetValue<string>()));
        Assert.Equal("Predicate in user", items[0]!["detail"]!.GetValue<string>());
        Assert.Null(items[0]!["documentation"]);
        workspace.Close(Uri);
        Assert.Empty(workspace.Completion(Uri, 0, 0));
        Assert.Empty(workspace.Completion("untitled:unknown", 0, 0));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void IncompleteWordsWorkAcrossLineEndings(string newline)
    {
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
        workspace.Update(new DocumentSnapshot(Uri, "/* comment */" + newline + "run :- mem", 1));
        Assert.Contains(workspace.Completion(Uri, 1, 10), item => item!["label"]!.GetValue<string>() == "member/2");
    }

    [Theory]
    [InlineData("run :- % mem")]
    [InlineData("run :- /* mem")]
    [InlineData("run :- 'mem")]
    [InlineData("run :- \"mem")]
    [InlineData("run :- absent:mem")]
    [InlineData("run(M) :- M:mem")]
    public void CommentsLiteralsAndUnknownModulesHaveNoSuggestions(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
        workspace.Update(new DocumentSnapshot(Uri, text, 1));
        Assert.Empty(workspace.Completion(Uri, 0, text.Length));
    }

    [Fact]
    public void ProjectMembershipAndModeRefreshCandidates()
    {
        string folder = Path.Combine(Path.GetTempPath(), "completion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string source = Path.Combine(folder, "main.pl");
            string library = Path.Combine(folder, "library.pl");
            File.WriteAllText(source, "run :- mem");
            File.WriteAllText(library, "member(_, _).");
            string uri = new System.Uri(source).AbsoluteUri;
            var project = new ProjectConfiguration(
                Path.Combine(folder, "app.dplproj"),
                PrologLanguageMode.StrictIso,
                PrologFlagOverrides.None,
                [source],
                []
            );
            var workspace = new DocumentWorkspace(PrologLanguageMode.Modern, project);
            workspace.StartProject();
            Assert.Empty(workspace.Completion(uri, 0, 10));
            workspace.ReplaceProject(project with { Sources = [source, library] });
            Assert.Equal("Predicate in user", Assert.Single(workspace.Completion(uri, 0, 10))!["detail"]!.GetValue<string>());
            workspace.ReplaceProject(project with { Mode = PrologLanguageMode.Modern });
            Assert.Contains(workspace.Completion(uri, 0, 10), item => item!["detail"]!.GetValue<string>() == "Builtin predicate");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void SharedIncludeIntersectsConflictingContextsAndHonorsBufferOverlay()
    {
        string folder = Path.Combine(Path.GetTempPath(), "completion-include-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string child = Path.Combine(folder, "child.pl");
            File.WriteAllText(child, "run :- mem.");
            string childUri = new System.Uri(child).AbsoluteUri;
            string first = new System.Uri(Path.Combine(folder, "first.pl")).AbsoluteUri;
            string second = new System.Uri(Path.Combine(folder, "second.pl")).AbsoluteUri;
            var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
            workspace.Update(new DocumentSnapshot(first, ":- module(a, []). local. :- include('child.pl').", 1));
            Assert.Contains(workspace.Completion(childUri, 0, 10), item => item!["label"]!.GetValue<string>() == "member/2");
            workspace.Update(new DocumentSnapshot(second, ":- module(b, []). member(_, _). :- include('child.pl').", 1));
            Assert.DoesNotContain(
                workspace.Completion(childUri, 0, 10),
                item => item!["label"]!.GetValue<string>() == "member/2"
            );
            workspace.Close(second);
            workspace.Update(
                new DocumentSnapshot(
                    first,
                    ":- module(a, []). local. :- include('child.pl'). :- module(b, []). member(_, _). :- include('child.pl').",
                    2
                )
            );
            Assert.DoesNotContain(
                workspace.Completion(childUri, 0, 10),
                item => item!["label"]!.GetValue<string>() == "member/2"
            );
            workspace.Update(new DocumentSnapshot(first, ":- module(a, []). local. :- include('child.pl').", 3));
            Assert.Contains(workspace.Completion(childUri, 0, 10), item => item!["label"]!.GetValue<string>() == "member/2");
            workspace.Update(new DocumentSnapshot(childUri, "run :- loc", 1));
            Assert.Contains(workspace.Completion(childUri, 0, 10), item => item!["label"]!.GetValue<string>() == "local/0");
            const string buffer = "run(Value) :- f(Va";
            workspace.Update(new DocumentSnapshot(childUri, buffer, 2));
            Assert.Equal("Value", Assert.Single(workspace.Completion(childUri, 0, buffer.Length))!["label"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static JsonObject Reply(List<JsonObject> messages, int id) =>
        messages.Single(item => item["id"]?.ToJsonString() == id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string Request(int id, int line, int character) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "textDocument/completion",
            ["params"] = new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = Uri },
                ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
            },
        }.ToJsonString();
}
