using System.Text.Json.Nodes;
using DotProlog.Runtime;

namespace DotProlog.LanguageServer.Tests;

public sealed class DocumentSymbolTests
{
    private const string Initialize =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{"textDocument":{"documentSymbol":{"hierarchicalDocumentSymbolSupport":true}}}}}""";
    private const string Shutdown = """{"jsonrpc":"2.0","id":99,"method":"shutdown"}""";
    private const string Exit = """{"jsonrpc":"2.0","method":"exit"}""";

    [Fact]
    public async Task HierarchicalOutlineGroupsClausesAndPreservesDcgArityAndUnicodeSelections()
    {
        const string uri = "untitled:outline";
        const string text = ":- module(m, []).\r\n'p😀'(a).\r\n'p😀'(b) :- true.\r\np --> [x].\r\np(X) --> [X].\r\nbroken(";
        var (_, messages) = await ProtocolTests.RunServerAsync(
            new LanguageServer(debounce: TimeSpan.Zero),
            Initialize,
            Open(uri, text),
            Request(uri, 2),
            Shutdown,
            Exit
        );
        Assert.True(messages[0]["result"]!["capabilities"]!["documentSymbolProvider"]!.GetValue<bool>());
        var symbols = messages.Single(item => item["id"]?.ToJsonString() == "2")["result"]!.AsArray();
        Assert.Equal(["m:p😀/1", "m:p//0", "m:p//1"], symbols.Select(item => item!["name"]!.GetValue<string>()));
        var first = symbols[0]!;
        Assert.Equal(2, first["children"]!.AsArray().Count);
        Assert.Equal(1, first["selectionRange"]!["start"]!["line"]!.GetValue<int>());
        Assert.Equal(5, first["selectionRange"]!["end"]!["character"]!.GetValue<int>());
        Assert.Equal(2, first["range"]!["end"]!["line"]!.GetValue<int>());
        Assert.Equal("DCG", symbols[2]!["detail"]!.GetValue<string>());
    }

    [Fact]
    public async Task FlatClientsReceiveLocationsAndInvalidOrUnknownDocumentsAreHandled()
    {
        const string uri = "untitled:flat";
        var (_, messages) = await ProtocolTests.RunServerAsync(
            new LanguageServer(debounce: TimeSpan.Zero),
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{}}}""",
            Open(uri, "p. p(a)."),
            Request(uri, 2),
            Request("not a URI", 3),
            Request("untitled:unknown", 4),
            Shutdown,
            Exit
        );
        var symbols = messages.Single(item => item["id"]?.ToJsonString() == "2")["result"]!.AsArray();
        Assert.Equal(["p/0", "p/1"], symbols.Select(item => item!["name"]!.GetValue<string>()));
        Assert.All(symbols, item => Assert.Equal(uri, item!["location"]!["uri"]!.GetValue<string>()));
        Assert.Equal(-32602, messages.Single(item => item["id"]?.ToJsonString() == "3")["error"]!["code"]!.GetValue<int>());
        Assert.Empty(messages.Single(item => item["id"]?.ToJsonString() == "4")["result"]!.AsArray());
    }

    [Fact]
    public void IncludedFileUsesInheritedOperatorsAndRetainsOnlyItsOwnDefinitions()
    {
        string folder = Path.Combine(Path.GetTempPath(), "DotProlog-symbols-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string child = Path.Combine(folder, "child.pl");
            string root = Path.Combine(folder, "root.pl");
            File.WriteAllText(child, "alice likes bob.");
            var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
            workspace.Update(
                new DocumentSnapshot(
                    new Uri(root).AbsoluteUri,
                    ":- module(m, []). :- op(500, xfy, likes). :- include('child.pl'). root.",
                    1
                )
            );
            string childUri = new Uri(child).AbsoluteUri;
            var symbols = workspace.Symbols(childUri, true);
            Assert.Equal("m:likes/2", Assert.Single(symbols)!["name"]!.GetValue<string>());
            Assert.Equal(6, symbols[0]!["selectionRange"]!["start"]!["character"]!.GetValue<int>());
            Assert.Equal(
                "m:root/0",
                Assert.Single(workspace.Symbols(new Uri(root).AbsoluteUri, true))!["name"]!.GetValue<string>()
            );
            workspace.Update(new DocumentSnapshot(childUri, "carol likes dan.", 1));
            Assert.Single(workspace.Symbols(childUri, true));
            workspace.Update(new DocumentSnapshot(childUri, "changed.", 2));
            Assert.Equal("m:changed/0", Assert.Single(workspace.Symbols(childUri, true))!["name"]!.GetValue<string>());
            workspace.Close(childUri);
            Assert.Equal("m:likes/2", Assert.Single(workspace.Symbols(childUri, true))!["name"]!.GetValue<string>());
            workspace.Close(new Uri(root).AbsoluteUri);
            Assert.Empty(workspace.Symbols(childUri, true));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ClosedProjectSourcesUseConfiguredModeAndMembership()
    {
        string folder = Path.Combine(Path.GetTempPath(), "DotProlog-outline-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "source.pl");
            File.WriteAllText(path, "disk. value(`modern`).");
            var project = new ProjectConfiguration(
                Path.Combine(folder, "app.dplproj"),
                PrologLanguageMode.StrictIso,
                PrologFlagOverrides.None,
                [path],
                []
            );
            var workspace = new DocumentWorkspace(PrologLanguageMode.Modern, project);
            workspace.StartProject();
            string uri = new Uri(path).AbsoluteUri;
            Assert.Equal("disk/0", Assert.Single(workspace.Symbols(uri, true))!["name"]!.GetValue<string>());
            workspace.Update(new DocumentSnapshot(uri, "unsaved.", 1));
            Assert.Equal("unsaved/0", Assert.Single(workspace.Symbols(uri, true))!["name"]!.GetValue<string>());
            workspace.Close(uri);
            Assert.Equal("disk/0", Assert.Single(workspace.Symbols(uri, true))!["name"]!.GetValue<string>());
            workspace.ReplaceProject(new ProjectConfiguration(project.ProjectPath, project.Mode, project.Flags, [], []));
            Assert.Empty(workspace.Symbols(uri, true));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static string Open(string uri, string text) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "textDocument/didOpen",
            ["params"] = new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = uri,
                    ["version"] = 1,
                    ["text"] = text,
                },
            },
        }.ToJsonString();

    private static string Request(string uri, int id) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "textDocument/documentSymbol",
            ["params"] = new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } },
        }.ToJsonString();
}
