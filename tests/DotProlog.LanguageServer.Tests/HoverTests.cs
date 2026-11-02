using System.Text.Json.Nodes;
using DotProlog.Runtime;

namespace DotProlog.LanguageServer.Tests;

public sealed class HoverTests
{
    private const string Uri = "untitled:hover";
    private const string Shutdown = """{"jsonrpc":"2.0","id":99,"method":"shutdown"}""";
    private const string Exit = """{"jsonrpc":"2.0","method":"exit"}""";

    [Theory]
    [InlineData("[]", "plaintext")]
    [InlineData("[\"markdown\",\"plaintext\"]", "markdown")]
    [InlineData("[\"plaintext\",\"markdown\"]", "plaintext")]
    [InlineData("[\"unsupported\",\"markdown\"]", "markdown")]
    public async Task ProtocolNegotiatesFormatAndReturnsExactUtf16NameRange(string formats, string kind)
    {
        string initialize =
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{"textDocument":{"hover":{"contentFormat":FORMATS}}}}}""".Replace(
                "FORMATS",
                formats,
                StringComparison.Ordinal
            );
        const string line = "run :- write('😀'), 'member'(a, []).";
        int start = line.IndexOf("'member'", StringComparison.Ordinal);
        var (_, messages) = await ProtocolTests.RunServerAsync(
            new LanguageServer(debounce: TimeSpan.Zero),
            initialize,
            Open("% comment\r\n" + line),
            Request(2, 1, start + 1),
            Shutdown,
            Exit
        );
        Assert.True(messages[0]["result"]!["capabilities"]!["hoverProvider"]!.GetValue<bool>());
        var hover = Reply(messages, 2)["result"]!;
        Assert.Equal(kind, hover["contents"]!["kind"]!.GetValue<string>());
        Assert.Contains("member/2", hover["contents"]!["value"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("list member", hover["contents"]!["value"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(start, hover["range"]!["start"]!["character"]!.GetValue<int>());
        Assert.Equal(start + 8, hover["range"]!["end"]!["character"]!.GetValue<int>());
        Assert.Equal(1, hover["range"]!["start"]!["line"]!.GetValue<int>());
    }

    [Fact]
    public async Task ProtocolRejectsMalformedPositionsAndReturnsNullForNoTarget()
    {
        var (_, messages) = await ProtocolTests.RunServerAsync(
            new LanguageServer(debounce: TimeSpan.Zero),
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{}}}""",
            Open("run :- write(member(a, []))."),
            Request(2, -1, 0),
            Request(3, 0, -1),
            Request(4, 0, int.MaxValue),
            Request(5, 10, 0),
            Request(6, 0, 13),
            Request(7, 0, 0),
            """{"jsonrpc":"2.0","id":8,"method":"textDocument/hover","params":{"textDocument":{"uri":"not a URI"},"position":{"line":0,"character":0}}}""",
            """{"jsonrpc":"2.0","id":9,"method":"textDocument/hover","params":{"textDocument":{"uri":"untitled:hover"},"position":{"line":"0","character":0}}}""",
            Shutdown,
            Exit
        );
        foreach (int id in new[] { 2, 3, 8, 9 })
            Assert.Equal(-32602, Reply(messages, id)["error"]!["code"]!.GetValue<int>());
        foreach (int id in new[] { 4, 5, 6, 7 })
            Assert.Null(Reply(messages, id)["result"]);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void PositionsHandleAllLineEndingsAndTokenBoundaries(string newline)
    {
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
        workspace.Update(new DocumentSnapshot(Uri, "/* write(data) */" + newline + "run :- write(ok).", 1));
        Assert.Null(workspace.Hover(Uri, 0, 2, false));
        Assert.Null(workspace.Hover(Uri, 1, 6, false));
        Assert.NotNull(workspace.Hover(Uri, 1, 7, false));
        Assert.NotNull(workspace.Hover(Uri, 1, 11, false));
        Assert.Null(workspace.Hover(Uri, 1, 12, false));
        Assert.Null(workspace.Hover(Uri, 1, int.MaxValue, false));
        workspace.Close(Uri);
        Assert.Null(workspace.Hover(Uri, 1, 7, false));
    }

    [Fact]
    public void IncludesUseParentOperatorsAndSuppressConflictingModuleResolutions()
    {
        string folder = Path.Combine(Path.GetTempPath(), "hover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string child = Path.Combine(folder, "child.pl");
            File.WriteAllText(child, "run :- a member [].");
            string childUri = new System.Uri(child).AbsoluteUri;
            string first = new System.Uri(Path.Combine(folder, "first.pl")).AbsoluteUri;
            string second = new System.Uri(Path.Combine(folder, "second.pl")).AbsoluteUri;
            var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
            const string includes = ":- op(700, xfx, member). :- include('child.pl').";
            workspace.Update(new DocumentSnapshot(first, ":- module(a, []). " + includes, 1));
            Assert.NotNull(workspace.Hover(childUri, 0, 9, false));
            workspace.Update(new DocumentSnapshot(second, ":- module(b, []). member(_, _). " + includes, 1));
            Assert.Null(workspace.Hover(childUri, 0, 9, false));
            workspace.Close(second);
            Assert.NotNull(workspace.Hover(childUri, 0, 9, false));
            workspace.Update(new DocumentSnapshot(childUri, "run :- write(ok).", 1));
            Assert.Contains(
                "write/1",
                workspace.Hover(childUri, 0, 7, false)!["contents"]!["value"]!.GetValue<string>(),
                StringComparison.Ordinal
            );
            workspace.Close(childUri);
            Assert.NotNull(workspace.Hover(childUri, 0, 9, false));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ProjectModeAndCrossFileOverridesRefreshHover()
    {
        string folder = Path.Combine(Path.GetTempPath(), "hover-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string source = Path.Combine(folder, "main.pl");
            string library = Path.Combine(folder, "library.pl");
            File.WriteAllText(source, "run :- member(a, []).");
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
            Assert.Contains(
                "StrictIso",
                workspace.Hover(uri, 0, 7, false)!["contents"]!["value"]!.GetValue<string>(),
                StringComparison.Ordinal
            );
            workspace.ReplaceProject(project with { Sources = [source, library] });
            Assert.Null(workspace.Hover(uri, 0, 7, false));
            workspace.ReplaceProject(project with { Mode = PrologLanguageMode.Modern });
            Assert.DoesNotContain(
                "StrictIso",
                workspace.Hover(uri, 0, 7, false)!["contents"]!["value"]!.GetValue<string>(),
                StringComparison.Ordinal
            );
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static JsonObject Reply(List<JsonObject> messages, int id) =>
        messages.Single(item => item["id"]?.ToJsonString() == id.ToString(System.Globalization.CultureInfo.InvariantCulture));

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
            ["method"] = "textDocument/hover",
            ["params"] = new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = Uri },
                ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
            },
        }.ToJsonString();
}
