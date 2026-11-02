using System.Text.Json.Nodes;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.LanguageServer.Tests;

public sealed class ProjectReloadTests : IDisposable
{
    private const string Initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{}}}""";
    private const string Initialized = """{"jsonrpc":"2.0","method":"initialized","params":{}}""";
    private const string Shutdown = """{"jsonrpc":"2.0","id":2,"method":"shutdown"}""";
    private const string Exit = """{"jsonrpc":"2.0","method":"exit"}""";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DotProlog reload", Guid.NewGuid().ToString("N"));

    public ProjectReloadTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ReplacementPreservesBuffersAndReanalyzesRemovedOpenFilesAsLooseFiles()
    {
        string member = Source("member.pl", "disk(ok).");
        var modern = Configuration([member]);
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern, modern);
        workspace.StartProject();
        workspace.Update(new DocumentSnapshot(new Uri(member).AbsoluteUri, "value(`text`).", 5));
        var strict = workspace.ReplaceProject(modern with { Mode = PrologLanguageMode.StrictIso });
        Assert.Equal(5, For(strict, member).Version);
        Assert.Contains(For(strict, member).Diagnostics, item => item!["severity"]!.GetValue<int>() == 1);
        var removed = workspace.ReplaceProject(modern with { Mode = PrologLanguageMode.StrictIso, Sources = [] });
        Assert.Equal(5, For(removed, member).Version);
        Assert.DoesNotContain(For(removed, member).Diagnostics, item => item!["severity"]!.GetValue<int>() == 1);
    }

    [Fact]
    public void ReplacementAddsNewSourcesAndClearsRemovedClosedSources()
    {
        string oldSource = Source("old.pl", "old(X).");
        string newSource = Source("new.pl", "new(Y).");
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern, Configuration([oldSource]));
        workspace.StartProject();
        var changes = workspace.ReplaceProject(Configuration([newSource]));
        Assert.Empty(For(changes, oldSource).Diagnostics);
        Assert.Contains(
            For(changes, newSource).Diagnostics,
            item => item!["code"]!.GetValue<string>() == LintDiagnosticIds.SingletonVariable
        );
    }

    [Fact]
    public async Task FailedReevaluationKeepsLastValidSettingsSuppressesRepeatedErrorsAndRecovers()
    {
        string member = Source("member.pl", "value(`text`).");
        var initial = Configuration([member]);
        var calls = 0;
        var server = new LanguageServer(
            PrologLanguageMode.Modern,
            initial,
            _ =>
            {
                calls++;
                return calls <= 2
                    ? Task.FromException<ProjectConfiguration>(new InvalidDataException("Invalid flags"))
                    : Task.FromResult(initial with { Mode = PrologLanguageMode.StrictIso });
            }
        );
        var changed = Changed(new Uri(Path.Combine(_directory, "settings.custom")).AbsoluteUri);
        (_, List<JsonObject> messages) = await ProtocolTests.RunServerAsync(
            server,
            Initialize,
            Initialized,
            changed,
            changed,
            Open(member, "value(`unsaved`).", 4),
            changed,
            Shutdown,
            Exit
        );
        Assert.Equal(3, calls);
        var logs = messages.Where(item => item["method"]?.GetValue<string>() == "window/logMessage").ToArray();
        Assert.Equal(2, logs.Length);
        Assert.Equal(1, logs[0]["params"]!["type"]!.GetValue<int>());
        Assert.Equal(3, logs[1]["params"]!["type"]!.GetValue<int>());
        var diagnostics = messages
            .Where(item => item["method"]?.GetValue<string>() == "textDocument/publishDiagnostics")
            .ToArray();
        Assert.DoesNotContain(
            diagnostics[1]["params"]!["diagnostics"]!.AsArray(),
            item => item!["severity"]!.GetValue<int>() == 1
        );
        Assert.Equal(4, diagnostics[^1]["params"]!["version"]!.GetValue<int>());
        Assert.Contains(diagnostics[^1]["params"]!["diagnostics"]!.AsArray(), item => item!["severity"]!.GetValue<int>() == 1);
    }

    [Fact]
    public async Task UnchangedSnapshotRefreshesDiskContentsAndEvaluatesOnlyOncePerValidBatch()
    {
        string member = Source("member.pl", "value(ok).");
        var initial = Configuration([member]);
        var calls = 0;
        var server = new LanguageServer(
            PrologLanguageMode.Modern,
            initial,
            async token =>
            {
                calls++;
                await File.WriteAllTextAsync(member, "value(X).", token);
                return initial;
            }
        );
        string uri = new Uri(member).AbsoluteUri;
        string invalid =
            """{"jsonrpc":"2.0","method":"workspace/didChangeWatchedFiles","params":{"changes":[{"uri":"file:///bad","type":99}]}}""";
        (_, List<JsonObject> messages) = await ProtocolTests.RunServerAsync(
            server,
            Initialize,
            Initialized,
            invalid,
            Changed("untitled:test.pl"),
            Changed(uri, uri),
            Shutdown,
            Exit
        );
        Assert.Equal(1, calls);
        Assert.Contains(
            messages.Where(item => item["method"]?.GetValue<string>() == "textDocument/publishDiagnostics").Last()["params"]![
                "diagnostics"
            ]!.AsArray(),
            item => item!["code"]!.GetValue<string>() == LintDiagnosticIds.SingletonVariable
        );
    }

    [Fact]
    public async Task FailedEvaluationStillAppliesDiskEditsUsingLastValidSnapshot()
    {
        string member = Source("member.pl", "value(ok).");
        var initial = Configuration([member]);
        var server = new LanguageServer(
            PrologLanguageMode.Modern,
            initial,
            async token =>
            {
                await File.WriteAllTextAsync(member, "value(X).", token);
                throw new IOException("SDK unavailable");
            }
        );
        (_, List<JsonObject> messages) = await ProtocolTests.RunServerAsync(
            server,
            Initialize,
            Initialized,
            Changed(new Uri(member).AbsoluteUri),
            Shutdown,
            Exit
        );
        Assert.Contains(
            messages.Last(item => item["method"]?.GetValue<string>() == "textDocument/publishDiagnostics")["params"]![
                "diagnostics"
            ]!.AsArray(),
            item => item!["code"]!.GetValue<string>() == LintDiagnosticIds.SingletonVariable
        );
    }

    private static string Changed(params string[] uris) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "workspace/didChangeWatchedFiles",
            ["params"] = new JsonObject
            {
                ["changes"] = new JsonArray(
                    uris.Select(uri => (JsonNode)new JsonObject { ["uri"] = uri, ["type"] = 2 }).ToArray()
                ),
            },
        }.ToJsonString();

    private static string Open(string path, string text, int version) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "textDocument/didOpen",
            ["params"] = new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = new Uri(path).AbsoluteUri,
                    ["text"] = text,
                    ["version"] = version,
                    ["languageId"] = "prolog",
                },
            },
        }.ToJsonString();

    private ProjectConfiguration Configuration(string[] sources) =>
        new(Path.Combine(_directory, "test.dplproj"), PrologLanguageMode.Modern, PrologFlagOverrides.None, sources, []);

    private string Source(string name, string text)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static DiagnosticPublication For(IReadOnlyList<DiagnosticPublication> publications, string path) =>
        Assert.Single(publications, item => item.Uri == new Uri(path).AbsoluteUri);

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
