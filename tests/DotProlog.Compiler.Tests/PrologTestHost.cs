using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.Compiler.Tests;

/// <summary>Consults a source unit and runs its goals with output captured, for end-to-end assertions.</summary>
internal static class PrologTestHost
{
    /// <summary>Consults and runs <paramref name="source"/>, asserting that it compiles and succeeds.</summary>
    internal static string Run(string source)
    {
        (RunResult result, var output, IReadOnlyList<Diagnostic> diagnostics) = Execute(source);

        Assert.Empty(diagnostics);
        Assert.Equal(RunResult.Success, result);
        return output;
    }

    /// <summary>Consults and runs <paramref name="source"/>, reporting the outcome without asserting.</summary>
    internal static (RunResult Result, string Output, IReadOnlyList<Diagnostic> Diagnostics) Execute(string source)
    {
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output };

        LoadResult loaded = engine.ConsultText(source, "test.pl");
        if (!loaded.Success)
        {
            return (RunResult.Failure, output.ToString(), loaded.Diagnostics);
        }

        RunResult result = engine.RunPendingGoals();
        return (result, output.ToString(), loaded.Diagnostics);
    }

    /// <summary>Wraps <paramref name="goal"/> in an initialization directive so it runs after loading.</summary>
    internal static string RunGoal(string goal) => Run($":- initialization(({goal})).\n");
}
