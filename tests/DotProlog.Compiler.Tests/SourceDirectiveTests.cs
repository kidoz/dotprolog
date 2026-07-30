using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>ISO source-file directives whose behavior depends on their exact preparation point.</summary>
public sealed class SourceDirectiveTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dotprolog-directives-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void IncludeSplicesClausesAtTheDirectivePosition()
    {
        Write(
            "included.pl",
            """
            included_fact(ok).
            :- op(600, xfx, likes).
            """
        );

        string main = Write(
            "main.pl",
            """
            before.
            :- include(included).
            alice likes prolog.
            :- initialization((before, included_fact(ok), alice likes prolog, write(ok))).
            """
        );

        Assert.Equal("ok", RunFile(main));
    }

    [Fact]
    public void IncludedCharacterConversionAffectsTheNextParentToken()
    {
        Write(
            "conversion.pl",
            """
            :- char_conversion(x, z).
            :- set_prolog_flag(char_conversion, on).
            """
        );

        string main = Write(
            "main.pl",
            """
            :- include(conversion).
            xray.
            :- initialization((zray, write(ok))).
            """
        );

        Assert.Equal("ok", RunFile(main));
    }

    [Fact]
    public void IncludeResolvesNestedPathsRelativeToTheIncludingFile()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "nested"));
        Write("leaf.pl", "leaf(ok).");
        Write("nested/middle.pl", ":- include('../leaf.pl').");
        string main = Write(
            "main.pl",
            """
            :- include('nested/middle.pl').
            :- initialization((leaf(ok), write(ok))).
            """
        );

        Assert.Equal("ok", RunFile(main));
    }

    [Fact]
    public void MissingIncludeProducesAStableDiagnostic()
    {
        var engine = new PrologEngine();
        string main = Write("main.pl", ":- include(missing).");

        LoadResult loaded = engine.ConsultFile(main);

        Assert.Equal(CompilerDiagnosticIds.IncludeNotFound, Assert.Single(loaded.Diagnostics).Id);
    }

    [Fact]
    public void RecursiveIncludeProducesAStableDiagnostic()
    {
        string main = Write("main.pl", ":- include(main).");
        var engine = new PrologEngine();

        LoadResult loaded = engine.ConsultFile(main);

        Assert.Equal(CompilerDiagnosticIds.IncludeCycle, Assert.Single(loaded.Diagnostics).Id);
    }

    [Fact]
    public void EnsureLoadedDirectiveLoadsOnceRelativeToItsSource()
    {
        Write("nested/dependency.pl", ":- initialization(write(dependency)).");
        string main = Write(
            "nested/main.pl",
            """
            :- ensure_loaded(dependency).
            :- ensure_loaded('./dependency.pl').
            :- initialization(write(main)).
            """
        );

        Assert.Equal("dependencymain", RunFile(main));
    }

    [Fact]
    public void RuntimeEnsureLoadedRemembersAPreviouslyConsultedFile()
    {
        string dependency = Write("dependency.pl", ":- initialization(write(dependency)).");
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output, Input = TextReader.Null };

        Assert.Empty(engine.ConsultFile(dependency).Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal(RunResult.Success, engine.RunGoal($"ensure_loaded('{dependency}')", out _));
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());

        Assert.Equal("dependency", output.ToString());
    }

    [Fact]
    public void MissingEnsureLoadedProducesAStableDiagnostic()
    {
        var engine = new PrologEngine();
        string main = Write("main.pl", ":- ensure_loaded(missing).");

        LoadResult loaded = engine.ConsultFile(main);

        Assert.Equal(CompilerDiagnosticIds.EnsureLoadedNotFound, Assert.Single(loaded.Diagnostics).Id);
    }

    private string Write(string name, string source)
    {
        string path = Path.Combine(_directory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        return path;
    }

    private static string RunFile(string path)
    {
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output, Input = TextReader.Null };

        LoadResult loaded = engine.ConsultFile(path);
        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        return output.ToString();
    }
}
