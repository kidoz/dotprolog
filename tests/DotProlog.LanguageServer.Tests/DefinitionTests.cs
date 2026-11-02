using System.Text.Json.Nodes;
using DotProlog.Runtime;

namespace DotProlog.LanguageServer.Tests;

public sealed class DefinitionTests
{
    private const string Uri = "untitled:definition";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtocolNegotiatesLinksAndReturnsEveryClauseWithUtf16Ranges(bool links)
    {
        const string head = "'has space'(a). 'has space'(b).";
        const string call = "run :- write('😀'), 'has space'(a).";
        int column = call.IndexOf("'has space'", StringComparison.Ordinal);
        var (_, messages) = await ProtocolTests.RunServerAsync(
            new LanguageServer(debounce: TimeSpan.Zero),
            Initialize(links),
            Open(head + "\r\n" + call),
            Request(2, 1, column + 1),
            """{"jsonrpc":"2.0","id":99,"method":"shutdown"}""",
            """{"jsonrpc":"2.0","method":"exit"}"""
        );
        Assert.True(messages[0]["result"]!["capabilities"]!["definitionProvider"]!.GetValue<bool>());
        var result = (JsonArray)Reply(messages, 2)["result"]!;
        Assert.Equal(2, result.Count);
        for (int index = 0; index < 2; index++)
        {
            var item = result[index]!;
            Assert.Equal(Uri, item[links ? "targetUri" : "uri"]!.GetValue<string>());
            var range = item[links ? "targetSelectionRange" : "range"]!;
            Assert.Equal(index * 16, range["start"]!["character"]!.GetValue<int>());
            Assert.Equal(index * 16 + 11, range["end"]!["character"]!.GetValue<int>());
            Assert.Equal(0, range["start"]!["line"]!.GetValue<int>());
            if (links)
            {
                Assert.Equal(column, item["originSelectionRange"]!["start"]!["character"]!.GetValue<int>());
                Assert.Equal(column + 11, item["originSelectionRange"]!["end"]!["character"]!.GetValue<int>());
                Assert.Equal(1, item["originSelectionRange"]!["start"]!["line"]!.GetValue<int>());
                Assert.True(item["targetRange"]!["end"]!["character"]!.GetValue<int>() > index * 16 + 11);
            }
        }
    }

    [Fact]
    public async Task MalformedPositionsAreRejectedAndUnknownTargetsAreEmpty()
    {
        var (_, messages) = await ProtocolTests.RunServerAsync(
            new LanguageServer(debounce: TimeSpan.Zero),
            Initialize(false),
            Open("run :- write(ok)."),
            Request(2, -1, 0),
            Request(3, 0, -1),
            Request(4, 0, 7),
            Request(5, 99, 0),
            Request(6, 0, int.MaxValue),
            """{"jsonrpc":"2.0","id":7,"method":"textDocument/definition","params":{"textDocument":{"uri":"not a URI"},"position":{"line":0,"character":0}}}""",
            """{"jsonrpc":"2.0","id":8,"method":"textDocument/definition","params":{"textDocument":{"uri":"untitled:definition"},"position":{"line":"0","character":0}}}""",
            """{"jsonrpc":"2.0","id":99,"method":"shutdown"}""",
            """{"jsonrpc":"2.0","method":"exit"}"""
        );
        foreach (int id in new[] { 2, 3, 7, 8 })
            Assert.Equal(-32602, Reply(messages, id)["error"]!["code"]!.GetValue<int>());
        foreach (int id in new[] { 4, 5, 6 })
            Assert.Empty((JsonArray)Reply(messages, id)["result"]!);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern, "\n")]
    [InlineData(PrologLanguageMode.StrictIso, "\r\n")]
    [InlineData(PrologLanguageMode.Modern, "\r")]
    public void UnsavedDefinitionsMoveWithEditsAndCloseRemovesTargets(PrologLanguageMode mode, string newline)
    {
        var workspace = new DocumentWorkspace(mode);
        workspace.Update(new DocumentSnapshot(Uri, "p(a)." + newline + "run :- p(a).", 1));
        Assert.Equal(0, Assert.Single(workspace.Definition(Uri, 1, 7, false))!["range"]!["start"]!["line"]!.GetValue<int>());
        workspace.Update(new DocumentSnapshot(Uri, "/* moved */" + newline + "p(a)." + newline + "run :- p(a).", 2));
        Assert.Equal(1, Assert.Single(workspace.Definition(Uri, 2, 7, false))!["range"]!["start"]!["line"]!.GetValue<int>());
        workspace.Update(new DocumentSnapshot(Uri, "p(a)." + newline + "run :- write(p(a)).", 3));
        Assert.Empty(workspace.Definition(Uri, 1, 13, false));
        workspace.Close(Uri);
        Assert.Empty(workspace.Definition(Uri, 0, 0, false));
        Assert.Empty(workspace.Definition("untitled:unknown", 0, 0, false));
    }

    [Fact]
    public void ProjectDefinitionsUseTargetBuffersAndRefreshMembership()
    {
        string folder = Path.Combine(Path.GetTempPath(), "definition-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string main = Path.Combine(folder, "main.pl");
            string library = Path.Combine(folder, "library space.pl");
            File.WriteAllText(main, "run :- item(X).");
            File.WriteAllText(library, "item(one). item(two).");
            string uri = new System.Uri(main).AbsoluteUri;
            string libraryUri = new System.Uri(library).AbsoluteUri;
            var project = new ProjectConfiguration(
                Path.Combine(folder, "app.dplproj"),
                PrologLanguageMode.Modern,
                PrologFlagOverrides.None,
                [main, library],
                []
            );
            var workspace = new DocumentWorkspace(PrologLanguageMode.Modern, project);
            workspace.StartProject();
            var locations = workspace.Definition(uri, 0, 7, false);
            Assert.Equal(2, locations.Count);
            Assert.All(locations, item => Assert.Equal(libraryUri, item!["uri"]!.GetValue<string>()));
            workspace.Update(new DocumentSnapshot(libraryUri, "/* 😀 */\r\nitem(edited).", 1));
            Assert.Equal(
                1,
                Assert.Single(workspace.Definition(uri, 0, 7, true))!["targetSelectionRange"]!["start"]!["line"]!.GetValue<int>()
            );
            workspace.Close(libraryUri);
            Assert.Equal(2, workspace.Definition(uri, 0, 7, false).Count);
            File.WriteAllText(library, "other.");
            workspace.DiskChanged([libraryUri]);
            Assert.Empty(workspace.Definition(uri, 0, 7, false));
            workspace.ReplaceProject(project with { Sources = [main] });
            Assert.Empty(workspace.Definition(uri, 0, 7, false));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void IncludesKeepPhysicalLocationsAndSuppressConflictingParents()
    {
        string folder = Path.Combine(Path.GetTempPath(), "definition-include-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string child = Path.Combine(folder, "child.pl");
            File.WriteAllText(child, "child. run :- local.");
            string childUri = new System.Uri(child).AbsoluteUri;
            string first = new System.Uri(Path.Combine(folder, "first.pl")).AbsoluteUri;
            string second = new System.Uri(Path.Combine(folder, "second.pl")).AbsoluteUri;
            var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
            workspace.Update(
                new DocumentSnapshot(first, ":- module(a, []). local. :- include('child.pl'). call_child :- child.", 1)
            );
            Assert.Equal(first, Assert.Single(workspace.Definition(childUri, 0, 14, false))!["uri"]!.GetValue<string>());
            Assert.Equal(childUri, Assert.Single(workspace.Definition(first, 0, 63, false))!["uri"]!.GetValue<string>());
            workspace.Update(new DocumentSnapshot(second, ":- module(b, []). local. :- include('child.pl').", 1));
            Assert.Empty(workspace.Definition(childUri, 0, 14, false));
            workspace.Close(second);
            Assert.Single(workspace.Definition(childUri, 0, 14, false));
            workspace.Update(
                new DocumentSnapshot(
                    first,
                    ":- module(a, []). local. :- include('child.pl'). :- module(b, []). local. :- include('child.pl').",
                    2
                )
            );
            Assert.Empty(workspace.Definition(childUri, 0, 14, false));
            workspace.Update(new DocumentSnapshot(childUri, "run :- hold(local).", 1));
            workspace.Update(
                new DocumentSnapshot(
                    first,
                    ":- module(a, []). local. hold(_). :- include('child.pl'). :- module(b, []). local. :- meta_predicate(hold(0)). hold(G) :- call(G). :- include('child.pl').",
                    3
                )
            );
            Assert.Empty(workspace.Definition(childUri, 0, 12, false));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static string Initialize(bool links) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "initialize",
            ["params"] = new JsonObject
            {
                ["capabilities"] = new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["definition"] = new JsonObject { ["linkSupport"] = links } },
                },
            },
        }.ToJsonString();

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
                    ["version"] = 1,
                    ["text"] = text,
                },
            },
        }.ToJsonString();

    private static string Request(int id, int line, int character) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "textDocument/definition",
            ["params"] = new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = Uri },
                ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
            },
        }.ToJsonString();

    private static JsonObject Reply(List<JsonObject> messages, int id) =>
        messages.Single(item => item["id"]?.ToJsonString() == id.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
