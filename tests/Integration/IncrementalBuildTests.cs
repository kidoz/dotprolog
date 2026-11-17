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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepositorySdkBuildsTheAnalyzerBeforeItsFirstCompilation(bool publishAot)
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );
        await BuildTaskAssembly();
        WriteProjectFile();
        WriteModule("rules", "Rules");
        var configuration = "CleanAnalyzer" + Guid.NewGuid().ToString("N");
        var analyzer = Path.Combine(
            RepositoryLayout.Root,
            "src",
            "DotProlog.Foreign.SourceGenerator",
            "bin",
            configuration,
            "netstandard2.0",
            "DotProlog.Foreign.SourceGenerator.dll"
        );
        Assert.False(File.Exists(analyzer));
        var task = Path.Combine(
            RepositoryLayout.Root,
            "src",
            "DotProlog.Build.Tasks",
            "bin",
            "Debug",
            "net10.0",
            "DotProlog.Build.Tasks.dll"
        );

        var (exit, log) = await Build(
            "-c",
            configuration,
            "-p:DotPrologTasksAssembly=" + task,
            "-p:PublishAot=" + publishAot.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );

        Assert.True(exit == 0, $"Building with no existing analyzer failed:\n{log}");
        Assert.True(File.Exists(analyzer));
        Assert.True(File.Exists(Path.Combine(_project, "bin", configuration, "net10.0", "Incremental.dll")));
    }

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
        var unrelated = Path.Combine(generatedPath, "Other.g.cs");
        await File.WriteAllTextAsync(unrelated, "// unrelated generator output", TestContext.Current.CancellationToken);
        var generatedAt = File.GetLastWriteTimeUtc(stamp);

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
        Assert.True(File.Exists(unrelated), "Regeneration deleted another generator output.");
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

        // The removed extended mode is an unknown name, not a silent fallback.
        (var removedExit, var removedLog) = await Build("-p:DotPrologLanguageMode=extended");
        Assert.True(removedExit != 0, "The removed extended mode was accepted.");
        Assert.Contains("'extended' is not a known Prolog language mode", removedLog, StringComparison.Ordinal);

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
        Assert.True(File.Exists(unrelated), "Clean deleted another generator output.");
        File.Delete(unrelated);
        Assert.Empty(Directory.GetFiles(generatedPath, "*.g.cs", SearchOption.AllDirectories));
        var (unsafeExit, unsafeLog) = await Build("-p:DotPrologGeneratedPath=" + _project + Path.DirectorySeparatorChar);
        Assert.NotEqual(0, unsafeExit);
        Assert.Contains("must not be the project directory", unsafeLog, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_project, "pricing.pl")));
    }

    [Theory]
    [InlineData("Library")]
    [InlineData("Exe")]
    [InlineData("Test")]
    public async Task SourceErrorsUsePositionedMsBuildDiagnostics(string kind)
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run toolchain tests."
        );
        (var taskExit, var taskLog) = await Run(
            "dotnet",
            ["build", "-nodereuse:false", Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Build.Tasks"), "--nologo"]
        );
        Assert.True(taskExit == 0, taskLog);
        WriteProjectFile();
        WriteModule("rules", "Rules");
        await File.WriteAllTextAsync(Path.Combine(_project, "rules.pl"), "\nvalue(.\n", TestContext.Current.CancellationToken);
        string[] properties = kind switch
        {
            "Exe" => ["-p:OutputType=Exe"],
            "Test" => ["-p:DotPrologTestProject=true"],
            _ => [],
        };
        (var exit, var log) = await Build(properties);
        Assert.NotEqual(0, exit);
        Assert.Contains("rules.pl(2,7): error DPL0003", log, StringComparison.Ordinal);
        Assert.DoesNotContain("MSB4018", log, StringComparison.Ordinal);
        Assert.DoesNotContain("ArgumentException", log, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.Combine(_project, "obj", "prolog"), "*.g.cs"));
    }

    [Fact]
    public async Task ContractOutputWarningsUsePositionedMsBuildDiagnosticsWithoutExecution()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );
        (var taskExit, var taskLog) = await Run(
            "dotnet",
            ["build", "-nodereuse:false", Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Build.Tasks"), "--nologo"]
        );
        Assert.True(taskExit == 0, taskLog);
        WriteProjectFile();
        WriteModule("rules", "Rules");
        await File.WriteAllTextAsync(
            Path.Combine(_project, "rules.dpli"),
            ":- clr_module('Rules').\n:- clr_export(value/1, det, [out(value, integer)]).\n",
            TestContext.Current.CancellationToken
        );
        await File.WriteAllTextAsync(
            Path.Combine(_project, "canary.pl"),
            ":- module(canary, []).\n:- initialization(halt(9)).\n",
            TestContext.Current.CancellationToken
        );
        const string incompatible = ":- module(m, [value/1]).\nvalue(wrong).\n";
        await File.WriteAllTextAsync(Path.Combine(_project, "rules.pl"), incompatible, TestContext.Current.CancellationToken);

        (var exit, var log) = await Build();
        Assert.True(exit == 0, $"The default build failed:\n{log}");
        Assert.Contains("rules.pl(2,7): warning DPL3052", log, StringComparison.Ordinal);
        Assert.Contains("Output value of m:value/1", log, StringComparison.Ordinal);

        await File.WriteAllTextAsync(Path.Combine(_project, "rules.pl"), incompatible, TestContext.Current.CancellationToken);
        (var strictExit, var strictLog) = await Build("-p:MSBuildTreatWarningsAsErrors=true");
        Assert.NotEqual(0, strictExit);
        Assert.Contains("error DPL3052", strictLog, StringComparison.Ordinal);

        await File.WriteAllTextAsync(
            Path.Combine(_project, "rules.pl"),
            ":- module(m, [value/1]).\nvalue(1). value(wrong).\n",
            TestContext.Current.CancellationToken
        );
        (var laterExit, var laterLog) = await Build();
        Assert.True(laterExit == 0, $"The later-fact build failed:\n{laterLog}");
        Assert.DoesNotContain("DPL3052", laterLog, StringComparison.Ordinal);

        await File.WriteAllTextAsync(
            Path.Combine(_project, "rules.pl"),
            ":- module(m, [value/1]).\n:- initialization(halt(9)).\nvalue(wrong).\n",
            TestContext.Current.CancellationToken
        );
        (var effectExit, var effectLog) = await Build();
        Assert.True(effectExit == 0, $"The effectful-module build failed:\n{effectLog}");
        Assert.DoesNotContain("DPL3052", effectLog, StringComparison.Ordinal);

        await File.WriteAllTextAsync(
            Path.Combine(_project, "rules.dpli"),
            ":- clr_module('Rules').\n:- clr_export(value/2, det, [in(item, integer), out(value, integer)]).\n",
            TestContext.Current.CancellationToken
        );
        await File.WriteAllTextAsync(
            Path.Combine(_project, "rules.pl"),
            ":- module(m, [value/2]).\nvalue(1, 1).\nvalue(2, wrong).\n",
            TestContext.Current.CancellationToken
        );
        (var laterDefaultExit, var laterDefaultLog) = await Build();
        Assert.True(laterDefaultExit == 0, $"The later-fact default build failed:\n{laterDefaultLog}");
        Assert.Contains("rules.pl(3,10): warning DPL3053", laterDefaultLog, StringComparison.Ordinal);
        Assert.Contains("Output value of m:value/2", laterDefaultLog, StringComparison.Ordinal);

        await File.WriteAllTextAsync(
            Path.Combine(_project, "rules.pl"),
            ":- module(m, [value/2]).\nvalue(1, 1) :- a = b.\nvalue(1, wrong) :- true.\n",
            TestContext.Current.CancellationToken
        );
        (var groundRuleExit, var groundRuleLog) = await Build();
        Assert.True(groundRuleExit == 0, $"The ground-rule default build failed:\n{groundRuleLog}");
        Assert.Contains("rules.pl(3,10): warning DPL3054", groundRuleLog, StringComparison.Ordinal);
        Assert.Contains("Output value of m:value/2", groundRuleLog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EffectivePropertiesAndMissingOutputsInvalidateGeneration()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );
        await BuildTaskAssembly();
        WriteProjectFile();
        WriteModule("rules", "Rules");

        var generatedPath = Path.Combine(_project, "obj", "prolog");
        var facade = Path.Combine(generatedPath, "RulesModule.g.cs");
        var entryPoint = Path.Combine(generatedPath, "PrologProgram.g.cs");

        (var libraryExit, var libraryLog) = await Build();
        Assert.True(libraryExit == 0, $"The library build failed:\n{libraryLog}");
        Assert.Contains("namespace Incremental.Rules;", await Read(facade), StringComparison.Ordinal);
        Assert.False(File.Exists(entryPoint));

        (var namespaceExit, var namespaceLog) = await Build("-p:RootNamespace=Changed.Rules");
        Assert.True(namespaceExit == 0, $"The namespace-change build failed:\n{namespaceLog}");
        Assert.Contains("namespace Changed.Rules;", await Read(facade), StringComparison.Ordinal);

        File.Delete(facade);
        (var missingExit, var missingLog) = await Build("-p:RootNamespace=Changed.Rules");
        Assert.True(missingExit == 0, $"The missing-output build failed:\n{missingLog}");
        Assert.True(File.Exists(facade), "A deleted facade was not regenerated.");
        Assert.Contains("namespace Changed.Rules;", await Read(facade), StringComparison.Ordinal);

        await WriteEffectiveProperties("<OutputType>Exe</OutputType>");
        (var executableExit, var executableLog) = await Build();
        Assert.True(executableExit == 0, $"The executable build failed:\n{executableLog}");
        Assert.True(File.Exists(entryPoint), "Changing OutputType to Exe did not generate an entry point.");
        Assert.Contains("namespace Incremental.Rules;", await Read(entryPoint), StringComparison.Ordinal);

        File.Delete(entryPoint);
        (var missingEntryExit, var missingEntryLog) = await Build();
        Assert.True(missingEntryExit == 0, $"The missing-entry-point build failed:\n{missingEntryLog}");
        Assert.True(File.Exists(entryPoint), "A deleted entry point was not regenerated.");

        await WriteEffectiveProperties("<DotPrologTestProject>true</DotPrologTestProject>");
        (var testExit, var testLog) = await Build();
        Assert.True(testExit == 0, $"The test-project build failed:\n{testLog}");
        Assert.Contains("DotProlog.Testing.PrologTestHost.RunAsync", await Read(entryPoint), StringComparison.Ordinal);

        await WriteEffectiveProperties(string.Empty);
        (var restoredExit, var restoredLog) = await Build();
        Assert.True(restoredExit == 0, $"The restored library build failed:\n{restoredLog}");
        Assert.Contains("namespace Incremental.Rules;", await Read(facade), StringComparison.Ordinal);
        Assert.False(File.Exists(entryPoint), "Returning to a library left the stale entry point behind.");
    }

    [Fact]
    public async Task SameBasenameSourcesAndContractsPairByRelativePath()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );
        await BuildTaskAssembly();
        WriteProjectFile();
        WriteModule("first", "rules", "FirstRules", "alpha_marker");
        WriteModule("second", "rules", "SecondRules", "omega_marker");

        (var exit, var log) = await Build();
        Assert.True(exit == 0, $"The duplicate-basename build failed:\n{log}");
        var generatedPath = Path.Combine(_project, "obj", "prolog");
        var first = await Read(Path.Combine(generatedPath, "FirstRulesModule.g.cs"));
        var second = await Read(Path.Combine(generatedPath, "SecondRulesModule.g.cs"));
        Assert.Contains("alpha_marker", first, StringComparison.Ordinal);
        Assert.DoesNotContain("omega_marker", first, StringComparison.Ordinal);
        Assert.Contains("omega_marker", second, StringComparison.Ordinal);
        Assert.DoesNotContain("alpha_marker", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AmbiguousSourcePairsAndGeneratedFilenameCollisionsAreRejected()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );
        await BuildTaskAssembly();
        WriteProjectFile();
        WriteModule("rules", "Rules");
        var project = Path.Combine(_project, "Incremental.dplproj");
        var text = await Read(project);
        await File.WriteAllTextAsync(
            project,
            text.Replace(
                "<ProjectReference",
                "<PrologCompile Include=\"rules.pl\" />\n                    <ProjectReference",
                StringComparison.Ordinal
            ),
            TestContext.Current.CancellationToken
        );

        (var ambiguousExit, var ambiguousLog) = await Build();
        Assert.NotEqual(0, ambiguousExit);
        Assert.Contains("Multiple PrologCompile items match 'rules.pl'", ambiguousLog, StringComparison.Ordinal);
        Assert.DoesNotContain("MSB4018", ambiguousLog, StringComparison.Ordinal);

        WriteProjectFile();
        WriteModule("first", "Shared");
        WriteModule("second", "Shared");
        await File.WriteAllTextAsync(
            Path.Combine(_project, "first.dpli"),
            ":- clr_namespace('First.Namespace').\n:- clr_module('Shared').\n"
                + ":- clr_export(value/1, nondet, [out(value, atom)]).\n",
            TestContext.Current.CancellationToken
        );
        await File.WriteAllTextAsync(
            Path.Combine(_project, "second.dpli"),
            ":- clr_namespace('Second.Namespace').\n:- clr_module('Shared').\n"
                + ":- clr_export(value/1, nondet, [out(value, atom)]).\n",
            TestContext.Current.CancellationToken
        );

        (var collisionExit, var collisionLog) = await Build();
        Assert.NotEqual(0, collisionExit);
        Assert.Contains("both generate 'SharedModule.g.cs'", collisionLog, StringComparison.Ordinal);
        Assert.Contains("distinct clr_module/1 name", collisionLog, StringComparison.Ordinal);
        Assert.DoesNotContain("MSB4018", collisionLog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DesignTimeBuildSkipsGenerationAndInvalidPackageConfigurationIsATaskError()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Set {OptInVariable}=1 to run the toolchain integration tests."
        );
        await BuildTaskAssembly();
        WriteProjectFile();
        WriteModule("rules", "Rules");
        await File.WriteAllTextAsync(
            Path.Combine(_project, "propack.json"),
            "{\"name\":\"example/app\"}\n",
            TestContext.Current.CancellationToken
        );
        var environment = new Dictionary<string, string?> { ["PROPACK_CACHE"] = Path.Combine(_project, "cache") };
        var project = Path.Combine(_project, "Incremental.dplproj");

        (var designTimeExit, var designTimeLog) = await Run(
            "dotnet",
            [
                "msbuild",
                "-nodereuse:false",
                project,
                "--nologo",
                "-target:CoreCompile",
                "-p:DesignTimeBuild=true",
                "-p:SkipCompilerExecution=true",
                "-p:BuildProjectReferences=false",
            ],
            environment
        );
        Assert.True(designTimeExit == 0, $"The design-time build failed:\n{designTimeLog}");
        Assert.DoesNotContain("Package configuration is invalid", designTimeLog, StringComparison.Ordinal);
        var generatedPath = Path.Combine(_project, "obj", "prolog");
        Assert.False(
            Directory.Exists(generatedPath) && Directory.EnumerateFiles(generatedPath, "*.g.cs").Any(),
            "The design-time build generated C# sources."
        );

        (var invalidExit, var invalidLog) = await Run("dotnet", ["build", "-nodereuse:false", project, "--nologo"], environment);
        Assert.NotEqual(0, invalidExit);
        Assert.Contains("Package configuration is invalid", invalidLog, StringComparison.Ordinal);
        Assert.Contains("outside the project", invalidLog, StringComparison.Ordinal);
        Assert.DoesNotContain("MSB4018", invalidLog, StringComparison.Ordinal);
    }

    private static Task<string> Read(string path) => File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

    private static async Task BuildTaskAssembly()
    {
        (var exit, var log) = await Run(
            "dotnet",
            ["build", "-nodereuse:false", Path.Combine(RepositoryLayout.Root, "src", "DotProlog.Build.Tasks"), "--nologo"]
        );
        Assert.True(exit == 0, $"Building the task failed:\n{log}");
    }

    private Task WriteEffectiveProperties(string properties) =>
        File.WriteAllTextAsync(
            Path.Combine(_project, "Directory.Build.props"),
            $"<Project><PropertyGroup>{properties}</PropertyGroup></Project>\n",
            TestContext.Current.CancellationToken
        );

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
                    <ProjectReference Include="{source}/DotProlog.Testing/DotProlog.Testing.csproj" />
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

    private void WriteModule(string directory, string module, string clrName, string value)
    {
        Directory.CreateDirectory(Path.Combine(_project, directory));
        File.WriteAllText(Path.Combine(_project, directory, $"{module}.pl"), $"value({value}).\n");
        File.WriteAllText(
            Path.Combine(_project, directory, $"{module}.dpli"),
            $":- clr_module('{clrName}').\n:- clr_export(value/1, nondet, [out(value, atom)]).\n"
        );
    }

    private Task<(int ExitCode, string Log)> Build(params string[] properties) =>
        Run("dotnet", ["build", "-nodereuse:false", Path.Combine(_project, "Incremental.dplproj"), "--nologo", .. properties]);

    private static Task<(int ExitCode, string Log)> Run(
        string fileName,
        string[] arguments,
        IReadOnlyDictionary<string, string?>? environment = null
    ) => ChildProcess.RunAsync(fileName, arguments, RepositoryLayout.Root, environment);
}
