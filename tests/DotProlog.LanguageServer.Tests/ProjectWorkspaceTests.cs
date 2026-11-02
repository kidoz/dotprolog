using System.Text.Json.Nodes;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.LanguageServer.Tests;

public sealed class ProjectWorkspaceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DotProlog project", Guid.NewGuid().ToString("N"));

    public ProjectWorkspaceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ProjectUsesConfiguredFlagsAndResolvesUnopenedSourceFiles()
    {
        string caller = Source("caller.pl", "run :- helper, atom_codes(_, \"abc\").");
        string helper = Source("helper.pl", "helper.");
        var workspace = new DocumentWorkspace(
            PrologLanguageMode.Modern,
            Configuration([caller, helper], PrologLanguageMode.Modern, DoubleQuotesMode.Codes)
        );
        var initial = workspace.StartProject();
        Assert.DoesNotContain(
            For(initial, caller).Diagnostics,
            item =>
                item!["code"]!.GetValue<string>()
                    is LintDiagnosticIds.UndefinedPredicate
                        or LintDiagnosticIds.DoubleQuotedListKindMismatch
        );
        var changed = workspace.Update(new DocumentSnapshot(new Uri(helper).AbsoluteUri, "other.", 1));
        Assert.Contains(
            For(changed, caller).Diagnostics,
            item => item!["code"]!.GetValue<string>() == LintDiagnosticIds.UndefinedPredicate
        );
        Assert.DoesNotContain(
            For(workspace.Close(new Uri(helper).AbsoluteUri), caller).Diagnostics,
            item => item!["code"]!.GetValue<string>() == LintDiagnosticIds.UndefinedPredicate
        );
    }

    [Fact]
    public void ProjectModeAppliesToMembersAndLooseFilesKeepDefaultMode()
    {
        string member = Source("member.pl", "value(`text`).");
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern, Configuration([member], PrologLanguageMode.StrictIso));
        Assert.Contains(For(workspace.StartProject(), member).Diagnostics, item => item!["severity"]!.GetValue<int>() == 1);
        string loose = Path.Combine(_directory, "loose.pl");
        Assert.DoesNotContain(
            For(workspace.Update(new DocumentSnapshot(new Uri(loose).AbsoluteUri, "value(`text`).", 1)), loose).Diagnostics,
            item => item!["severity"]!.GetValue<int>() == 1
        );
    }

    [Fact]
    public void ProjectSourcesRefreshOnDiskChangesAndMissingSourcesAreReported()
    {
        string member = Source("member.pl", "value(X).");
        var workspace = new DocumentWorkspace(PrologLanguageMode.Modern, Configuration([member], PrologLanguageMode.Modern));
        workspace.StartProject();
        File.Delete(member);
        Assert.Contains(
            For(workspace.DiskChanged([new Uri(member).AbsoluteUri]), member).Diagnostics,
            item => item!["code"]!.GetValue<string>() == LintDiagnosticIds.UnresolvedLintSource
        );
        File.WriteAllText(member, "value(ok).");
        Assert.DoesNotContain(
            For(workspace.DiskChanged([new Uri(member).AbsoluteUri]), member).Diagnostics,
            item => item!["code"]!.GetValue<string>() == LintDiagnosticIds.UnresolvedLintSource
        );
    }

    [Theory]
    [InlineData("nonsense", "", "")]
    [InlineData("modern", "unknown=true", "")]
    [InlineData("strict-iso", "double_quotes=string", "")]
    [InlineData("modern", "", "net9.0;net10.0")]
    public void InvalidProjectSettingsAreRejected(string mode, string flags, string frameworks)
    {
        var json = new JsonObject
        {
            ["Properties"] = new JsonObject
            {
                ["DotPrologLanguageMode"] = mode,
                ["DotPrologFlags"] = flags,
                ["TargetFrameworks"] = frameworks,
                ["MSBuildProjectFullPath"] = Path.Combine(_directory, "test.dplproj"),
            },
            ["Items"] = new JsonObject { ["PrologCompile"] = new JsonArray(), ["PrologContract"] = new JsonArray() },
        };
        Assert.Throws<InvalidDataException>(() => ProjectConfiguration.Parse(json.ToJsonString()));
    }

    private ProjectConfiguration Configuration(string[] sources, PrologLanguageMode mode, DoubleQuotesMode? quotes = null) =>
        new(Path.Combine(_directory, "test.dplproj"), mode, new PrologFlagOverrides { DoubleQuotes = quotes }, sources, []);

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
