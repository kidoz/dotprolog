using DotProlog.Runtime;

namespace DotProlog.Compiler.Tests;

/// <summary>The first acceptance target: the shipped sample prints its greeting.</summary>
public sealed class HelloWorldTests
{
    [Fact]
    public void SampleFilePrintsItsGreeting()
    {
        var output = new StringWriter();
        var engine = new PrologEngine { Output = output };

        LoadResult loaded = engine.ConsultFile("hello.pl");
        Assert.True(loaded.Success, string.Join("; ", loaded.Diagnostics));
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());

        Assert.Equal("Hello! World!\n", output.ToString());
    }

    [Fact]
    public void InlineHelloWorldPrintsItsGreeting()
    {
        var output = PrologTestHost.Run(
            """
            :- initialization(main).

            main :- write('Hello! World!'), nl.
            """
        );

        Assert.Equal("Hello! World!\n", output);
    }
}
