namespace Integration.Tests;

/// <summary>
/// Builds a temporary <c>.dplproj</c> twice and then renames its module, proving that an unchanged
/// build skips facade generation, that a rename deletes the stale facade instead of compiling it,
/// and that <c>dotnet clean</c> removes the generated files.
/// </summary>
/// <remarks>
/// Incremental behaviour is MSBuild behaviour, so like <see cref="ProjectReferenceTests"/> this can
/// only be checked by running MSBuild. The rename case matters because it is invisible to
/// timestamps: the old files are simply absent from the glob.
/// </remarks>
public sealed class IncrementalBuildTests : IDisposable
{
    private const string OptInVariable = "DOTPROLOG_RUN_AOT_TESTS";

    private readonly string _project = CreateProjectDirectory();

    private static string CreateProjectDirectory()
    {
        var path = Directory.CreateTempSubdirectory("dotprolog-incremental").FullName;

        // macOS puts temporary files under /var, a symlink to /private/var. MSBuild computes
        // relative paths between projects from the literal strings, which then miss by one level,
        // so the real path is what the project file has to live at.
        return OperatingSystem.IsMacOS() && path.StartsWith("/var/", StringComparison.Ordinal) ? "/private" + path : path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_project, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing the run over.
        }
    }

    [Fact]
    public async Task GenerationIsSkippedWhenNothingChangedAndARenamedModuleLeavesNoStaleFacade()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );

        // The task assembly is loaded by MSBuild, so it has to exist before the project builds.
        (var taskExit, var taskLog) = await Run(
            "dotnet",
            ["build", "-nodereuse:false", Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Build.Tasks"), "--nologo"]
        );

        Assert.True(taskExit == 0, $"Building the task failed:\n{taskLog}");

        WriteProjectFile();
        WriteModule("rules", "Rules");

        var generatedPath = Path.Combine(_project, "obj", "prolog");
        var stamp = Path.Combine(generatedPath, ".generated");
        var rulesFacade = Path.Combine(generatedPath, "RulesModule.g.cs");

        (var firstExit, var firstLog) = await Build();
        Assert.True(firstExit == 0, $"The first build failed:\n{firstLog}");
        Assert.True(File.Exists(rulesFacade), $"No generated facade at {rulesFacade}.");
        DateTime generatedAt = File.GetLastWriteTimeUtc(stamp);

        // Nothing changed, so the stamp the generation target touches must not move.
        (var secondExit, var secondLog) = await Build();
        Assert.True(secondExit == 0, $"The second build failed:\n{secondLog}");
        Assert.Equal(generatedAt, File.GetLastWriteTimeUtc(stamp));

        // A rename changes no surviving file's timestamp; only the recorded input list sees it.
        File.Delete(Path.Combine(_project, "rules.pl"));
        File.Delete(Path.Combine(_project, "rules.dpli"));
        WriteModule("pricing", "Pricing");

        (var thirdExit, var thirdLog) = await Build();
        Assert.True(thirdExit == 0, $"The build after the rename failed:\n{thirdLog}");
        Assert.True(File.Exists(Path.Combine(generatedPath, "PricingModule.g.cs")), "The renamed module was not generated.");
        Assert.False(File.Exists(rulesFacade), $"The stale facade {rulesFacade} survived the rename.");

        // A deletion is the truly timestamp-invisible change: every file that remains is untouched,
        // and only the recorded input list can tell this build from the one before it.
        WriteModule("extra", "Extra");
        var extraFacade = Path.Combine(generatedPath, "ExtraModule.g.cs");

        (var fourthExit, var fourthLog) = await Build();
        Assert.True(fourthExit == 0, $"The build with a second module failed:\n{fourthLog}");
        Assert.True(File.Exists(extraFacade), $"No generated facade at {extraFacade}.");

        File.Delete(Path.Combine(_project, "extra.pl"));
        File.Delete(Path.Combine(_project, "extra.dpli"));

        (var fifthExit, var fifthLog) = await Build();
        Assert.True(fifthExit == 0, $"The build after the deletion failed:\n{fifthLog}");
        Assert.False(File.Exists(extraFacade), $"The stale facade {extraFacade} survived the deletion.");

        // The language mode is an incremental input even when no project file or Prolog source
        // timestamp changes. Strict generation rejects extensions and records the mode in the
        // generated installer and engine construction.
        await File.WriteAllTextAsync(
            Path.Combine(_project, "pricing.pl"),
            "value(one) :- member(one, [one]).\n",
            TestContext.Current.CancellationToken
        );
        (var rejectedExit, var rejectedLog) = await Build("-p:DotPrologLanguageMode=strict-iso");
        Assert.True(rejectedExit != 0, "Strict generation accepted a bundled extension.");
        Assert.Contains("DPL1018", rejectedLog, StringComparison.Ordinal);

        await File.WriteAllTextAsync(
            Path.Combine(_project, "pricing.pl"),
            "value(one).\n",
            TestContext.Current.CancellationToken
        );
        (var strictExit, var strictLog) = await Build("-p:DotPrologLanguageMode=strict-iso");
        Assert.True(strictExit == 0, $"The strict build failed:\n{strictLog}");
        Assert.Contains(
            "PrologLanguageMode.StrictIso",
            await File.ReadAllTextAsync(Path.Combine(generatedPath, "PricingModule.g.cs"), TestContext.Current.CancellationToken),
            StringComparison.Ordinal
        );

        // A flag override is likewise an incremental input on its own: no file timestamp changes
        // between these builds, only the recorded property line. The override must reach the
        // generated engine construction and the install guard.
        (var flaggedExit, var flaggedLog) = await Build("-p:DotPrologFlags=double_quotes=atom");
        Assert.True(flaggedExit == 0, $"The flagged build failed:\n{flaggedLog}");
        var flaggedFacade = await File.ReadAllTextAsync(
            Path.Combine(generatedPath, "PricingModule.g.cs"),
            TestContext.Current.CancellationToken
        );
        Assert.Contains("DoubleQuotesMode.Atom", flaggedFacade, StringComparison.Ordinal);
        Assert.Contains("requires double_quotes to start at atom", flaggedFacade, StringComparison.Ordinal);

        (var badFlagExit, var badFlagLog) = await Build("-p:DotPrologFlags=double_quotes=strings");
        Assert.True(badFlagExit != 0, "An invalid DotPrologFlags value was accepted.");
        Assert.Contains("DotPrologFlags is invalid", badFlagLog, StringComparison.Ordinal);

        // The generated files live outside IntermediateOutputPath, so Clean needs its own proof.
        (var cleanExit, var cleanLog) = await Run(
            "dotnet",
            ["clean", "-nodereuse:false", Path.Combine(_project, "Incremental.dplproj"), "--nologo"]
        );

        Assert.True(cleanExit == 0, $"Cleaning failed:\n{cleanLog}");
        Assert.False(File.Exists(stamp), "Clean left the generation stamp behind.");
        Assert.Empty(Directory.GetFiles(generatedPath, "*.g.cs", SearchOption.AllDirectories));
    }

    private void WriteProjectFile()
    {
        var source = Path.Combine(RepositoryLayout.Root, "src");

        File.WriteAllText(
            Path.Combine(_project, "Incremental.dplproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
                <Import Project="{source}/DotProlog.Sdk/Sdk/Sdk.props" />
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <RootNamespace>Incremental.Rules</RootNamespace>
                    <NoWarn>$(NoWarn);CS1591</NoWarn>
                    <DotPrologTasksAssembly>{source}/DotProlog.Build.Tasks/bin/$(Configuration)/net10.0/DotProlog.Build.Tasks.dll</DotPrologTasksAssembly>
                </PropertyGroup>
                <ItemGroup>
                    <ProjectReference Include="{source}/DotProlog.Compiler/DotProlog.Compiler.csproj" />
                </ItemGroup>
                <Import Project="{source}/DotProlog.Sdk/Sdk/Sdk.targets" />
            </Project>
            """
        );
    }

    private void WriteModule(string module, string clrName)
    {
        File.WriteAllText(Path.Combine(_project, $"{module}.pl"), "value(one).\n");
        File.WriteAllText(
            Path.Combine(_project, $"{module}.dpli"),
            $":- clr_module('{clrName}').\n:- clr_export(value/1, nondet, [out(value, atom)]).\n"
        );
    }

    private Task<(int ExitCode, string Log)> Build(params string[] properties) =>
        Run("dotnet", ["build", "-nodereuse:false", Path.Combine(_project, "Incremental.dplproj"), "--nologo", .. properties]);

    private static Task<(int ExitCode, string Log)> Run(string fileName, string[] arguments) =>
        ChildProcess.RunAsync(fileName, arguments, RepositoryLayout.Root);
}
