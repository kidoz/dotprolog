using System.Runtime.InteropServices;

namespace Integration.Tests;

/// <summary>Exercises plc, emitted IL and NativeAOT as a real compiler toolchain.</summary>
public sealed class DirectIlCompilerTests
{
    [Fact]
    public async Task EmittedIlPublishesAndRunsWithoutItsSourceOrManagedAssemblies()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DOTPROLOG_RUN_AOT_TESTS") == "1",
            "Set DOTPROLOG_RUN_AOT_TESTS=1 to exercise the direct IL NativeAOT pipeline."
        );
        var root = Path.Combine(RepositoryLayout.Root, "tmp", "il-aot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source with spaces.pl");
            var output = Path.Combine(root, "output with spaces");
            await File.WriteAllTextAsync(
                source,
                """
                :- dynamic(value/1).
                value(initial).
                ancestor(X,Y) :- parent(X,Y).
                ancestor(X,Y) :- parent(X,Z), ancestor(Z,Y).
                parent(a,b).
                parent(b,c).
                count(0).
                count(N) :- N > 0, M is N-1, count(M).
                bound(a,b).
                main :-
                    findall(X,ancestor(a,X),[b,c]), count(10000),
                    findall(X-Y,parent(X,Y),[a-b,b-c]),
                    setup_call_cleanup(true,parent(b,c),Done=yes), Done==yes,
                    \+ parent(missing,_),
                    freeze(F, G=woke), bound(F,b), G==woke, \+ bound(a,c),
                    catch(throw(ball(done)),ball(done),true),
                    assertz(value(added)), retract(value(initial)),
                    findall(X,value(X),[added]),
                    consult('runtime.pl'),
                    findall(X,runtime_bridge(X),[b,c]),
                    writeln('direct-il-native: passed').
                :- initialization(main).
                """,
                TestContext.Current.CancellationToken
            );
            var compilerProject = Path.Combine(
                RepositoryLayout.Root,
                "src",
                "DotProlog.Compiler.Cli",
                "DotProlog.Compiler.Cli.csproj"
            );
            var (compileExit, compileLog) = await ChildProcess.RunAsync(
                "dotnet",
                [
                    "run",
                    "--project",
                    compilerProject,
                    "--",
                    source,
                    "--output",
                    output,
                    "--aot",
                    "--rid",
                    RuntimeInformation.RuntimeIdentifier,
                ],
                RepositoryLayout.Root
            );
            Assert.True(compileExit == 0, compileLog);
            Assert.DoesNotContain("warning IL", compileLog, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(output, "*.cs", SearchOption.AllDirectories));
            var isolated = Path.Combine(root, "isolated");
            Directory.CreateDirectory(isolated);
            var file = OperatingSystem.IsWindows() ? "PrologProgram.exe" : "PrologProgram";
            var executable = Path.Combine(isolated, file);
            File.Copy(Path.Combine(output, "native", file), executable);
            File.Delete(source);
            await File.WriteAllTextAsync(
                Path.Combine(isolated, "runtime.pl"),
                "runtime_bridge(X) :- ancestor(a,X).",
                TestContext.Current.CancellationToken
            );
            var (runExit, runLog) = await ChildProcess.RunAsync(executable, [], isolated);
            Assert.True(runExit == 0, runLog);
            Assert.Contains("direct-il-native: passed", runLog, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(isolated, "*.dll"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
