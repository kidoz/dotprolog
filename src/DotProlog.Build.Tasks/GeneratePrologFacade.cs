using DotProlog.CodeGen.CSharp;
using DotProlog.Compiler;
using DotProlog.Packages;
using DotProlog.Runtime;
using DotProlog.Syntax;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Task = Microsoft.Build.Utilities.Task;

namespace DotProlog.Build.Tasks;

/// <summary>
/// Reads each <c>.dpli</c> contract, generates the typed C# facade for its module, and reports the
/// generated files so the SDK can add them to the compilation.
/// </summary>
/// <remarks>
/// Contract and reader diagnostics are reported through MSBuild's logger with their file and
/// position, so a mistake in a <c>.dpli</c> or <c>.pl</c> appears as an ordinary build error rather
/// than as a runtime surprise.
/// </remarks>
public sealed class GeneratePrologFacade : Task
{
    /// <summary>The module's Prolog sources.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    /// <summary>The contracts declaring each module's .NET surface.</summary>
    [Required]
    public ITaskItem[] Contracts { get; set; } = [];

    /// <summary>The project directory used to pair sources and contracts by relative path.</summary>
    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    /// <summary>Directory the generated C# is written to, normally <c>obj/</c>.</summary>
    [Required]
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Namespace used when a contract does not declare one.</summary>
    public string RootNamespace { get; set; } = "Prolog.Generated";

    /// <summary>Whether to generate an entry point, set by the SDK when the project builds an application.</summary>
    public bool GenerateEntryPoint { get; set; }

    /// <summary>Whether that entry point should host the test platform rather than run the program.</summary>
    public bool GenerateTestHost { get; set; }

    /// <summary>The language mode generated source is built against; see <c>DotPrologLanguageMode</c>.</summary>
    public string LanguageMode { get; set; } = "modern";

    /// <summary>Initial flag overrides layered over the mode; see <c>DotPrologFlags</c>.</summary>
    public string Flags { get; set; } = string.Empty;

    /// <summary>Optional package workspace, restored explicitly before building.</summary>
    public string PackageDirectory { get; set; } = string.Empty;

    /// <summary>Files recorded by the previous successful generation.</summary>
    public ITaskItem[] PreviousGeneratedFiles { get; set; } = [];

    /// <summary>The generated C# files, to be added to <c>Compile</c>.</summary>
    [Output]
    public ITaskItem[] GeneratedFiles { get; private set; } = [];

    /// <inheritdoc />
    public override bool Execute()
    {
        var relativeProject = Path.GetRelativePath(Path.GetFullPath(OutputPath), Path.GetFullPath(ProjectDirectory));
        if (
            !Path.IsPathRooted(relativeProject)
            && relativeProject != ".."
            && !relativeProject.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        )
        {
            Log.LogError("DotPrologGeneratedPath must not be the project directory or one of its ancestors.");
            return false;
        }
        if (Contracts.Length == 0 && !GenerateEntryPoint)
        {
            Log.LogError(
                "A Prolog library needs a PrologContract item declaring its .NET surface. "
                    + "Set OutputType to Exe to build an application instead."
            );

            return false;
        }

        if (!PrologLanguageModes.TryParse(LanguageMode, out var languageMode))
        {
            Log.LogError(
                $"DotPrologLanguageMode '{LanguageMode}' is not a known Prolog language mode. "
                    + $"Expected one of: {PrologLanguageModes.Names}."
            );

            return false;
        }

        if (!PrologFlagOverrides.TryParse(Flags, out var flagOverrides, out var flagsError))
        {
            Log.LogError($"DotPrologFlags is invalid: {flagsError}");
            return false;
        }

        if (languageMode == PrologLanguageMode.StrictIso && flagOverrides.DoubleQuotes == DoubleQuotesMode.String)
        {
            Log.LogError("DotPrologFlags is invalid: double_quotes=string is not available in strict ISO mode.");
            return false;
        }

        IReadOnlyDictionary<string, string>? libraryFiles = null;
        if (PackageDirectory.Length > 0)
        {
            try
            {
                var version = typeof(GeneratePrologFacade).Assembly.GetName().Version!;
                libraryFiles = new PackageWorkspace(PackageDirectory).GetLibraries(
                    "dotprolog",
                    $"{version.Major}.{version.Minor}.{version.Build}"
                );
            }
            catch (ArgumentException exception)
            {
                Log.LogError($"Package configuration is invalid: {exception.Message}");
                return false;
            }
            catch (Exception exception)
                when (exception
                        is InvalidDataException
                            or IOException
                            or UnauthorizedAccessException
                            or System.Text.Json.JsonException
                )
            {
                Log.LogError($"Package validation failed: {exception.Message}");
                return false;
            }
        }
        Directory.CreateDirectory(OutputPath);
        List<ITaskItem> generated = [];
        List<PrologLintSource>? analysisSources = null;

        if (GenerateEntryPoint)
        {
            var sources = new List<(string Name, string Text)>();
            foreach (var source in Sources)
            {
                var path = source.GetMetadata("FullPath");
                sources.Add((path, File.ReadAllText(path)));
            }

            var entryPoint = Path.Combine(OutputPath, $"{EntryPointGenerator.TypeName}.g.cs");
            IReadOnlyList<Diagnostic> diagnostics;
            var sourceText = GenerateTestHost
                ? EntryPointGenerator.GenerateTestHost(
                    RootNamespace,
                    sources,
                    languageMode,
                    flagOverrides,
                    out diagnostics,
                    libraryFiles
                )
                : EntryPointGenerator.Generate(
                    RootNamespace,
                    sources,
                    languageMode,
                    flagOverrides,
                    out diagnostics,
                    libraryFiles
                );
            ReportDiagnostics(diagnostics, string.Empty);
            if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                WriteIfChanged(entryPoint, sourceText);
                generated.Add(new TaskItem(entryPoint));
            }
        }

        Dictionary<string, string> outputOwners = new(PathComparer);
        foreach (var contractItem in Contracts)
        {
            var contractPath = contractItem.GetMetadata("FullPath");

            if (!TryFindSource(contractPath, out var sourcePath))
            {
                continue;
            }

            var result = ContractReader.Read(File.ReadAllText(contractPath), RootNamespace, contractPath);
            ReportDiagnostics(result.Diagnostics, contractPath);

            if (!result.Success)
            {
                continue;
            }

            var target = Path.Combine(OutputPath, $"{result.Contract!.ClrTypeName}Module.g.cs");
            if (outputOwners.TryGetValue(target, out var existingContract))
            {
                Log.LogError(
                    $"Contracts '{existingContract}' and '{contractPath}' both generate '{Path.GetFileName(target)}'. "
                        + "Give each contract a distinct clr_module/1 name."
                );
                continue;
            }

            outputOwners.Add(target, contractPath);
            var facade = FacadeGenerator.Generate(
                result.Contract!,
                File.ReadAllText(sourcePath),
                sourcePath,
                languageMode,
                flagOverrides,
                out var sourceDiagnostics,
                libraryFiles
            );

            ReportDiagnostics(sourceDiagnostics, sourcePath);
            if (sourceDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                continue;
            }

            analysisSources ??= LoadAnalysisSources(languageMode, flagOverrides, libraryFiles);
            ReportDiagnostics(
                PrologContractLinter.Analyze(analysisSources, result.Contract!, contractPath, PrologLintOptions.SemanticOnly),
                sourcePath
            );

            WriteIfChanged(target, facade);
            generated.Add(new TaskItem(target));

            Log.LogMessage(MessageImportance.Normal, $"Generated {target} from {Path.GetFileName(contractPath)}.");
        }

        RemoveStaleOutputs(generated);

        GeneratedFiles = [.. generated];
        return !Log.HasLoggedErrors;
    }

    private List<PrologLintSource> LoadAnalysisSources(
        PrologLanguageMode languageMode,
        PrologFlagOverrides flagOverrides,
        IReadOnlyDictionary<string, string>? libraryFiles
    )
    {
        List<PrologLintSource> sources = [];
        HashSet<string> paths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var source in Sources)
        {
            Add(source.GetMetadata("FullPath"));
        }

        if (libraryFiles is not null)
        {
            foreach (var path in libraryFiles.Values)
            {
                Add(path);
            }
        }

        return sources;

        void Add(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!paths.Add(fullPath))
            {
                return;
            }

            var loaded = PrologLintSourceLoader.Load(
                fullPath,
                File.ReadAllText(fullPath),
                new BytecodeProgram(languageMode, flagOverrides)
            );
            sources.Add(loaded.Source);
        }
    }

    /// <summary>
    /// Deletes generated files a previous build wrote but this one did not, so a renamed or removed
    /// contract cannot leave its old facade behind to be compiled. Only manifest-owned files in
    /// the output directory may be deleted; other generators can share the directory.
    /// </summary>
    private void RemoveStaleOutputs(List<ITaskItem> generated)
    {
        HashSet<string> expected = new(PathComparer);
        foreach (var item in generated)
        {
            expected.Add(Path.GetFullPath(item.ItemSpec));
        }

        foreach (var file in PreviousGeneratedFiles)
        {
            var path = Path.GetFullPath(file.ItemSpec);
            if (
                !PathComparer.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(Path.GetFullPath(OutputPath)))
                || !path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            )
            {
                Log.LogError($"Generated output manifest contains an invalid path: {path}.");
                continue;
            }
            if (expected.Contains(path))
            {
                continue;
            }

            File.Delete(path);
            Log.LogMessage(MessageImportance.Normal, $"Deleted stale generated file {path}.");
        }
    }

    private bool TryFindSource(string contractPath, out string path)
    {
        var relativeContract = Path.GetRelativePath(ProjectDirectory, contractPath);
        var expectedSource = Path.ChangeExtension(relativeContract, ".pl");
        path = string.Empty;

        foreach (var source in Sources)
        {
            var candidate = source.GetMetadata("FullPath");
            var relativeSource = Path.GetRelativePath(ProjectDirectory, candidate);
            if (!PathComparer.Equals(relativeSource, expectedSource))
            {
                continue;
            }

            if (path.Length == 0)
            {
                path = candidate;
                continue;
            }

            Log.LogError(
                subcategory: null,
                errorCode: CodeGenDiagnosticIds.MissingModuleDeclaration,
                helpKeyword: null,
                file: contractPath,
                lineNumber: 0,
                columnNumber: 0,
                endLineNumber: 0,
                endColumnNumber: 0,
                message: $"Multiple PrologCompile items match '{expectedSource}'. Remove duplicate items so each contract has one source."
            );
            path = string.Empty;
            return false;
        }

        if (path.Length > 0)
        {
            return true;
        }

        Log.LogError(
            subcategory: null,
            errorCode: CodeGenDiagnosticIds.MissingModuleDeclaration,
            helpKeyword: null,
            file: contractPath,
            lineNumber: 0,
            columnNumber: 0,
            endLineNumber: 0,
            endColumnNumber: 0,
            message: $"No PrologCompile item at '{expectedSource}' matches this contract. "
                + "A source and contract with the same basename must have the same relative path."
        );
        return false;
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private void ReportDiagnostics(IReadOnlyList<Diagnostic> diagnostics, string defaultFile)
    {
        foreach (var diagnostic in diagnostics)
        {
            var file = diagnostic.FileName ?? defaultFile;

            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                Log.LogError(
                    null,
                    diagnostic.Id,
                    null,
                    file,
                    diagnostic.Span.Line,
                    diagnostic.Span.Column,
                    0,
                    0,
                    diagnostic.Message
                );

                continue;
            }

            Log.LogWarning(
                null,
                diagnostic.Id,
                null,
                file,
                diagnostic.Span.Line,
                diagnostic.Span.Column,
                0,
                0,
                diagnostic.Message
            );
        }
    }

    /// <summary>Writes only when the content differs, so an unchanged build stays incremental.</summary>
    private static void WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(path, content);
    }
}
