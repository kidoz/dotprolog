using System.Text.Json.Nodes;
using DotProlog.Runtime;

namespace DotProlog.LanguageServer.Tests;

public sealed class ReferenceTests : IDisposable
{
    private const string Uri = "untitled:references";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "references-" + Guid.NewGuid().ToString("N"));

    public ReferenceTests() => Directory.CreateDirectory(_folder);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtocolReturnsUtf16LocationsAndOptionallyAllHeads(bool declarations)
    {
        const string text = "'has space'(a). 'has space'(b).\r\nrun :- write('😀'), 'has space'(a), call('has space', b).";
        var (_, messages) = await ProtocolTests.RunServerAsync(
            new LanguageServer(debounce: TimeSpan.Zero),
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{}}}""",
            Open(text),
            Request(2, 0, 1, declarations),
            Request(3, 1, 21, declarations),
            """{"jsonrpc":"2.0","id":99,"method":"shutdown"}""",
            """{"jsonrpc":"2.0","method":"exit"}"""
        );
        Assert.True(messages[0]["result"]!["capabilities"]!["referencesProvider"]!.GetValue<bool>());
        var result = (JsonArray)Reply(messages, 2)["result"]!;
        Assert.Equal(declarations ? 4 : 2, result.Count);
        Assert.True(JsonNode.DeepEquals(result, Reply(messages, 3)["result"]));
        var calls = result.Where(item => item!["range"]!["start"]!["line"]!.GetValue<int>() == 1).ToArray();
        Assert.Equal(2, calls.Length);
        string line = text.Split('\n')[1];
        Assert.Equal(
            line.IndexOf("'has space'", StringComparison.Ordinal),
            calls[0]!["range"]!["start"]!["character"]!.GetValue<int>()
        );
        Assert.Equal(
            line.LastIndexOf("'has space'", StringComparison.Ordinal) + 11,
            calls[1]!["range"]!["end"]!["character"]!.GetValue<int>()
        );
        Assert.All(result, item => Assert.Equal(Uri, item!["uri"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"includeDeclaration\":null}")]
    [InlineData("{\"includeDeclaration\":\"false\"}")]
    [InlineData("null")]
    public async Task ProtocolRequiresBooleanDeclarationContext(string context)
    {
        var request = JsonNode.Parse(Request(2, 0, 0, false))!;
        request["params"]!["context"] = JsonNode.Parse(context);
        var (_, messages) = await ProtocolTests.RunServerAsync(
            new LanguageServer(debounce: TimeSpan.Zero),
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{}}}""",
            request.ToJsonString(),
            Request(3, -1, 0, false),
            Request(4, 0, -1, false),
            """{"jsonrpc":"2.0","id":99,"method":"shutdown"}""",
            """{"jsonrpc":"2.0","method":"exit"}"""
        );
        foreach (int id in new[] { 2, 3, 4 })
            Assert.Equal(-32602, Reply(messages, id)["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public void ModulesReexportsAndAritiesMatchSourceIdentity()
    {
        string library = Source("library.pl", ":- module(library, [item/1]). item(a). item(b). item(_, _).");
        string facade = Source("facade.pl", ":- module(facade, []). :- reexport(library, [item/1]).");
        const string clientText =
            ":- module(client, []). :- use_module('facade.pl'). run :- item(a), library:item(a,b), library:item(b), write(item(data)).";
        string client = Source("client.pl", clientText);
        string unrelated = Source("unrelated.pl", ":- module(other, []). item(a). run :- item(a).");
        var workspace = Project(client, facade, unrelated, library);
        var references = workspace.References(library, 0, 30, false);
        Assert.Equal(2, references.Count);
        Assert.All(references, item => Assert.Equal(client, item!["uri"]!.GetValue<string>()));
        Assert.Equal(4, workspace.References(client, 0, clientText.IndexOf("item(a)", StringComparison.Ordinal), true).Count);
        Assert.Single(workspace.References(unrelated, 0, 22, false));
    }

    [Fact]
    public void DcgAndMetaClosuresExcludeDataAndVariableCalls()
    {
        const string text =
            "word(X) --> [X]. start --> word(a), [word(data)]. run(G) :- phrase(word(a), []), call(word(a), [], []), write(word(data)), G.";
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
        workspace.Update(new DocumentSnapshot(Uri, text, 1));
        var references = workspace.References(Uri, 0, 0, false);
        Assert.Equal(3, references.Count);
        Assert.Equal(4, workspace.References(Uri, 0, 0, true).Count);
        Assert.Empty(workspace.References(Uri, 0, text.IndexOf("word(data)", StringComparison.Ordinal), false));
        Assert.Empty(workspace.References(Uri, 0, text.IndexOf("write", StringComparison.Ordinal), true));
    }

    [Fact]
    public void DeclaredMetaGoalsAndMultifileHeadsShareOnePredicate()
    {
        string first = Source(
            "one.pl",
            ":- multifile(p/1). p(one). :- meta_predicate(invoke(0)). invoke(G) :- call(G). run :- invoke(p(one))."
        );
        string second = Source("two.pl", ":- multifile(p/1). p(two). run2 :- p(two).");
        var workspace = Project(first, second);
        Assert.Equal(2, workspace.References(first, 0, 19, false).Count);
        Assert.Equal(4, workspace.References(second, 0, 19, true).Count);
    }

    [Theory]
    [InlineData(PrologLanguageMode.Modern, "\n")]
    [InlineData(PrologLanguageMode.StrictIso, "\r\n")]
    [InlineData(PrologLanguageMode.Modern, "\r")]
    public void UnsavedEditsCloseAndUnknownDocumentsRefreshIndex(PrologLanguageMode mode, string newline)
    {
        var workspace = new DocumentWorkspace(mode);
        workspace.Update(new DocumentSnapshot(Uri, "p." + newline + "run :- p.", 1));
        Assert.Equal(1, Assert.Single(workspace.References(Uri, 0, 0, false))!["range"]!["start"]!["line"]!.GetValue<int>());
        workspace.Update(new DocumentSnapshot(Uri, "p." + newline + "/* moved */" + newline + "run :- p, p.", 2));
        var references = workspace.References(Uri, 0, 0, false);
        Assert.Equal(2, references.Count);
        Assert.All(references, item => Assert.Equal(2, item!["range"]!["start"]!["line"]!.GetValue<int>()));
        workspace.Update(new DocumentSnapshot(Uri, "p. run :- write(p).", 3));
        Assert.Empty(workspace.References(Uri, 0, 0, false));
        Assert.Single(workspace.References(Uri, 0, 0, true));
        Assert.Empty(workspace.References(Uri, 99, 0, true));
        workspace.Close(Uri);
        Assert.Empty(workspace.References(Uri, 0, 0, true));
        Assert.Empty(workspace.References("untitled:unknown", 0, 0, false));
    }

    [Fact]
    public void IndependentLooseProgramsWithSamePredicateDoNotMix()
    {
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
        workspace.Update(new DocumentSnapshot(Uri, "p. run :- p.", 1));
        workspace.Update(new DocumentSnapshot("untitled:other", "p. other :- p, p.", 1));
        var references = workspace.References(Uri, 0, 0, true);
        Assert.Equal(2, references.Count);
        Assert.All(references, item => Assert.Equal(Uri, item!["uri"]!.GetValue<string>()));
    }

    [Fact]
    public void DiskChangesBufferOverlaysAndMembershipRefreshReferences()
    {
        string definition = Source("definition.pl", "p.");
        string caller = Source("caller.pl", "run :- p.");
        var workspace = Project(definition, caller);
        Assert.Single(workspace.References(definition, 0, 0, false));
        workspace.Update(new DocumentSnapshot(caller, "run :- p, p.", 1));
        Assert.Equal(2, workspace.References(definition, 0, 0, false).Count);
        File.WriteAllText(new System.Uri(caller).LocalPath, "run :- true.");
        workspace.DiskChanged([caller]);
        Assert.Equal(2, workspace.References(definition, 0, 0, false).Count);
        workspace.Close(caller);
        Assert.Empty(workspace.References(definition, 0, 0, false));
        File.WriteAllText(new System.Uri(caller).LocalPath, "run :- p.");
        workspace.DiskChanged([caller]);
        Assert.Single(workspace.References(definition, 0, 0, false));
        workspace.ReplaceProject(Configuration(definition));
        Assert.Empty(workspace.References(definition, 0, 0, false));
    }

    [Fact]
    public void SharedAndRepeatedIncludesDeduplicateAndOmitAmbiguousLocations()
    {
        string child = Source("child.pl", "run :- p.");
        string first = new System.Uri(Path.Combine(_folder, "first.pl")).AbsoluteUri;
        string second = new System.Uri(Path.Combine(_folder, "second.pl")).AbsoluteUri;
        const string includes = "p. :- include('child.pl'). :- include('child.pl').";
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
        workspace.Update(new DocumentSnapshot(first, includes, 1));
        Assert.Single(workspace.References(first, 0, 0, false));
        Assert.Equal(child, Assert.Single(workspace.References(first, 0, 0, false))!["uri"]!.GetValue<string>());
        workspace.Update(new DocumentSnapshot(second, "p. :- include('child.pl').", 1));
        // Both contexts use user:p/0 but refer to independent source definitions.
        Assert.Empty(workspace.References(first, 0, 0, false));
        Assert.Empty(workspace.References(child, 0, 7, true));
        Assert.Empty(workspace.Definition(child, 0, 7, false));
        workspace.Close(second);
        Assert.Single(workspace.References(first, 0, 0, false));
    }

    [Fact]
    public void SameIncludeTokenAsDataAndGoalIsNotReportedAsReference()
    {
        string child = Source("child.pl", "run :- hold(local).");
        const string text =
            ":- module(a, []). local. hold(_). :- include('child.pl'). :- module(b, []). local. :- meta_predicate(hold(0)). hold(G) :- call(G). :- include('child.pl').";
        string root = new System.Uri(Path.Combine(_folder, "root.pl")).AbsoluteUri;
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern);
        workspace.Update(new DocumentSnapshot(root, text, 1));
        Assert.Empty(workspace.References(child, 0, 12, true));
        Assert.Empty(workspace.References(root, 0, text.LastIndexOf("local.", StringComparison.Ordinal), false));
    }

    private string Source(string name, string text)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return new System.Uri(path).AbsoluteUri;
    }

    private ProjectConfiguration Configuration(params string[] uris) =>
        new(
            Path.Combine(_folder, "app.dplproj"),
            PrologLanguageMode.Modern,
            PrologFlagOverrides.None,
            uris.Select(uri => new System.Uri(uri).LocalPath).ToArray(),
            []
        );

    private DocumentWorkspace Project(params string[] uris)
    {
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern, Configuration(uris));
        workspace.StartProject();
        return workspace;
    }

    private static string Request(int id, int line, int character, bool declarations) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "textDocument/references",
            ["params"] = new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = Uri },
                ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
                ["context"] = new JsonObject { ["includeDeclaration"] = declarations },
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

    private static JsonObject Reply(List<JsonObject> messages, int id) =>
        messages.Single(item => item["id"]?.ToJsonString() == id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public void Dispose() => Directory.Delete(_folder, true);
}
