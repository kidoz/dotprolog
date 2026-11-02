using System.Text.Json.Nodes;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.LanguageServer.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DotProlog LSP # tests", Guid.NewGuid().ToString("N"));
    private readonly DocumentWorkspace _workspace = new(PrologLanguageMode.Modern);

    public WorkspaceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void UnsavedOperatorIncludeInvalidatesParentAndCloseRestoresDisk()
    {
        Write("operators.pl", ":- op(500, xfy, likes).\n");
        string parent = ":- include('operators.pl').\nalice likes bob.";
        var initial = Open("main.pl", parent);
        AssertNoErrors(For(initial, "main.pl"));
        var removed = Open("operators.pl", "");
        AssertHasErrors(For(removed, "main.pl"));
        var restored = _workspace.Close(UriFor("operators.pl"));
        AssertNoErrors(For(restored, "main.pl"));
        Assert.Null(For(restored, "operators.pl").Version);
    }

    [Fact]
    public void NestedMissingIncludesAreTrackedAndCanBeSuppliedWithoutSaving()
    {
        Write("middle.pl", ":- include('missing.pl').");
        var initial = Open("main.pl", ":- include('middle.pl').");
        AssertCode(For(initial, "middle.pl"), LintDiagnosticIds.UnresolvedLintSource);
        var repaired = Open("missing.pl", "included(X).");
        Assert.Empty(For(repaired, "middle.pl").Diagnostics);
        AssertCode(For(repaired, "missing.pl"), LintDiagnosticIds.SingletonVariable);
        Assert.False(File.Exists(Path.Combine(_directory, "missing.pl")));
        var closed = _workspace.Close(UriFor("missing.pl"));
        AssertCode(For(closed, "middle.pl"), LintDiagnosticIds.UnresolvedLintSource);
        Assert.Empty(For(closed, "missing.pl").Diagnostics);
    }

    [Fact]
    public void IncludedDiagnosticsUseTheirOwnTextAndUri()
    {
        Write("child space #.pl", "ok.\r\nvalue('😀', X).");
        var publications = Open("main.pl", ":- include('child space #.pl').");
        var child = For(publications, "child space #.pl");
        JsonNode singleton = child.Diagnostics.Single(item =>
            item!["code"]!.GetValue<string>() == LintDiagnosticIds.SingletonVariable
        )!;
        Assert.Equal(1, singleton["range"]!["start"]!["line"]!.GetValue<int>());
        Assert.Equal(12, singleton["range"]!["start"]!["character"]!.GetValue<int>());
        Assert.Null(child.Version);
    }

    [Fact]
    public void OpenIncludedDocumentUsesInheritedOperatorsInsteadOfStandaloneErrors()
    {
        const string child = "alice likes bob.";
        Write("child.pl", child);
        Open("main.pl", ":- op(500, xfy, likes).\n:- include('child.pl').");
        var publications = Open("child.pl", child);
        AssertNoErrors(For(publications, "child.pl"));
        Assert.Equal(1, For(publications, "child.pl").Version);
        var detached = _workspace.Close(UriFor("main.pl"));
        AssertHasErrors(For(detached, "child.pl"));
    }

    [Fact]
    public void RemovingIncludeClearsItsDiagnosticsAndUnrelatedRootsAreNotRepublished()
    {
        Write("child.pl", "value(X).");
        Open("main.pl", ":- include('child.pl').");
        Open("unrelated.pl", "ok.");
        var removed = Open("main.pl", "ok.", 2);
        Assert.Empty(For(removed, "child.pl").Diagnostics);
        Assert.DoesNotContain(removed, publication => publication.Uri == UriFor("unrelated.pl"));
        Assert.DoesNotContain(
            _workspace.Update(new DocumentSnapshot(UriFor("child.pl"), "value(Y).", 1)),
            publication => publication.Uri == UriFor("main.pl")
        );
    }

    [Fact]
    public void CyclesRemainBoundedAndCloseClearsAllOwnedDiagnostics()
    {
        Write("a.pl", ":- include('b.pl').");
        Write("b.pl", ":- include('a.pl').");
        var initial = Open("a.pl", ":- include('b.pl').");
        AssertCode(For(initial, "b.pl"), LintDiagnosticIds.UnresolvedLintSource);
        Open("b.pl", ":- include('a.pl').");
        _workspace.Close(UriFor("a.pl"));
        var closed = _workspace.Close(UriFor("b.pl"));
        Assert.NotEmpty(closed);
        Assert.All(closed, publication => Assert.Empty(publication.Diagnostics));
    }

    [Fact]
    public void OverlayIdentityNormalizesEscapingAndDotSegments()
    {
        Open("child #.pl", "value(X).");
        string alias = new Uri(Path.Combine(_directory, "sub", "..", "child #.pl")).AbsoluteUri;
        Assert.True(_workspace.IsNewer(alias, 2));
        var publications = Open("main.pl", ":- include('./child #.pl').");
        Assert.DoesNotContain(
            For(publications, "main.pl").Diagnostics,
            item => item!["code"]!.GetValue<string>() == LintDiagnosticIds.UnresolvedLintSource
        );
    }

    [Fact]
    public void SharedIncludesMergeDuplicateDiagnosticsAndRetainOtherOwners()
    {
        Write("child.pl", "value(X).");
        Open("a.pl", ":- include('child.pl').");
        var second = Open("b.pl", ":- include('child.pl').");
        Assert.DoesNotContain(second, publication => publication.Uri == UriFor("child.pl"));
        var closed = _workspace.Close(UriFor("a.pl"));
        Assert.DoesNotContain(closed, publication => publication.Uri == UriFor("child.pl"));
        Assert.Empty(For(closed, "a.pl").Diagnostics);
    }

    [Fact]
    public void FileAnalysisMatchesTheExistingLoaderAndLinter()
    {
        const string source = ":- include('child.pl').\nrun :- child(X), missing(X).";
        Write("child.pl", "child(ok).");
        string path = Path.Combine(_directory, "main.pl");
        var loaded = PrologLintSourceLoader.Load(path, source, new BytecodeProgram());
        JsonArray expected = DocumentDiagnostics.Convert(
            source,
            loaded.Diagnostics.Concat(PrologLinter.AnalyzeProgram([loaded.Source])).Where(item => item.FileName == path)
        );
        Assert.True(JsonNode.DeepEquals(expected, For(Open("main.pl", source), "main.pl").Diagnostics));
    }

    [Fact]
    public void DiskEditsInvalidateNestedDependentsAndPreserveBufferVersions()
    {
        Write("middle.pl", ":- include('operators.inc').");
        Write("operators.inc", ":- op(500, xfy, likes).");
        Open("main.pl", ":- include('middle.pl').\nalice likes bob.", 7);
        Open("unrelated.pl", "ok.");
        Write("operators.inc", "");
        var changed = _workspace.DiskChanged([UriFor("operators.inc"), UriFor("operators.inc")]);
        AssertHasErrors(For(changed, "main.pl"));
        Assert.Equal(7, For(changed, "main.pl").Version);
        Assert.DoesNotContain(changed, item => item.Uri == UriFor("unrelated.pl"));
        Assert.Empty(_workspace.DiskChanged([UriFor("operators.inc")]));
    }

    [Fact]
    public void DiskCreationRepairsMissingIncludeAndDeletionClearsChildDiagnostics()
    {
        Open("main.pl", ":- include('child.pl').");
        Write("child.pl", "value(X).");
        var created = _workspace.DiskChanged([UriFor("child.pl")]);
        Assert.Empty(For(created, "main.pl").Diagnostics);
        AssertCode(For(created, "child.pl"), LintDiagnosticIds.SingletonVariable);
        File.Delete(Path.Combine(_directory, "child.pl"));
        var deleted = _workspace.DiskChanged([UriFor("child.pl")]);
        AssertCode(For(deleted, "main.pl"), LintDiagnosticIds.UnresolvedLintSource);
        Assert.Empty(For(deleted, "child.pl").Diagnostics);
    }

    [Fact]
    public void DiskNotificationsNeverOverrideOpenBuffers()
    {
        Write("child.pl", "disk(X).");
        Open("main.pl", ":- include('child.pl').");
        Open("child.pl", "buffer(ok).");
        File.Delete(Path.Combine(_directory, "child.pl"));
        Assert.Empty(_workspace.DiskChanged([UriFor("child.pl"), UriFor("main.pl")]));
        Write("child.pl", "changed(Y).");
        Assert.Empty(_workspace.DiskChanged([UriFor("child.pl")]));
        AssertCode(For(_workspace.Close(UriFor("child.pl")), "child.pl"), LintDiagnosticIds.SingletonVariable);
    }

    [Fact]
    public void DiskBatchesRefreshSharedRootsAndReplaceDependencyGraph()
    {
        Write("middle.pl", ":- include('old.pl').");
        Write("old.pl", "old(X).");
        Open("a.pl", ":- include('middle.pl').");
        Open("b.pl", ":- include('middle.pl').");
        Write("middle.pl", ":- include('new.pl').");
        Write("new.pl", "new(Y).");
        var changed = _workspace.DiskChanged([UriFor("middle.pl"), UriFor("new.pl")]);
        Assert.Empty(For(changed, "old.pl").Diagnostics);
        AssertCode(For(changed, "new.pl"), LintDiagnosticIds.SingletonVariable);
        Write("old.pl", "broken(");
        Assert.Empty(_workspace.DiskChanged([UriFor("old.pl")]));
        Write("new.pl", "new(ok).");
        Assert.DoesNotContain(
            For(_workspace.DiskChanged([UriFor("new.pl")]), "new.pl").Diagnostics,
            item => item!["code"]!.GetValue<string>() == LintDiagnosticIds.SingletonVariable
        );
    }

    [Fact]
    public void UnrelatedAndVirtualDiskEventsDoNotPublishDiagnostics()
    {
        Open("main.pl", "ok.");
        Assert.Empty(_workspace.DiskChanged([UriFor("unrelated.pl"), "untitled:unrelated.pl"]));
        Assert.Empty(_workspace.DiskChanged([]));
    }

    private IReadOnlyList<DiagnosticPublication> Open(string file, string text, int version = 1) =>
        _workspace.Update(new DocumentSnapshot(UriFor(file), text, version));

    private string UriFor(string file) => new Uri(Path.Combine(_directory, file)).AbsoluteUri;

    private void Write(string file, string text) => File.WriteAllText(Path.Combine(_directory, file), text);

    private DiagnosticPublication For(IReadOnlyList<DiagnosticPublication> publications, string file) =>
        Assert.Single(publications, publication => publication.Uri == UriFor(file));

    private static void AssertCode(DiagnosticPublication publication, string code) =>
        Assert.Contains(publication.Diagnostics, item => item!["code"]!.GetValue<string>() == code);

    private static void AssertNoErrors(DiagnosticPublication publication) =>
        Assert.DoesNotContain(publication.Diagnostics, item => item!["severity"]!.GetValue<int>() == 1);

    private static void AssertHasErrors(DiagnosticPublication publication) =>
        Assert.Contains(publication.Diagnostics, item => item!["severity"]!.GetValue<int>() == 1);

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
