using DotProlog.CodeGen.CSharp;
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
    public string LanguageMode { get; set; } = "extended";

    /// <summary>Initial flag overrides layered over the mode; see <c>DotPrologFlags</c>.</summary>
    public string Flags { get; set; } = string.Empty;

    /// <summary>The generated C# files, to be added to <c>Compile</c>.</summary>
    [Output]
    public ITaskItem[] GeneratedFiles { get; private set; } = [];

    /// <inheritdoc />
    public override bool Execute()
    {
        if (Contracts.Length == 0 && !GenerateEntryPoint)
        {
            Log.LogError(
                "A Prolog library needs a PrologContract item declaring its .NET surface. "
                    + "Set OutputType to Exe to build an application instead."
            );

            return false;
        }

        if (!PrologLanguageModes.TryParse(LanguageMode, out PrologLanguageMode languageMode))
        {
            Log.LogError(
                $"DotPrologLanguageMode '{LanguageMode}' is not a known Prolog language mode. "
                    + $"Expected one of: {PrologLanguageModes.Names}."
            );

            return false;
        }

        if (!PrologFlagOverrides.TryParse(Flags, out PrologFlagOverrides flagOverrides, out var flagsError))
        {
            Log.LogError($"DotPrologFlags is invalid: {flagsError}");
            return false;
        }

        Directory.CreateDirectory(OutputPath);
        List<ITaskItem> generated = [];

        if (GenerateEntryPoint)
        {
            var sources = new List<(string Name, string Text)>();
            foreach (ITaskItem source in Sources)
            {
                var path = source.GetMetadata("FullPath");
                sources.Add((Path.GetFileName(path), File.ReadAllText(path)));
            }

            var entryPoint = Path.Combine(OutputPath, $"{EntryPointGenerator.TypeName}.g.cs");
            WriteIfChanged(
                entryPoint,
                GenerateTestHost
                    ? EntryPointGenerator.GenerateTestHost(RootNamespace, sources, languageMode, flagOverrides)
                    : EntryPointGenerator.Generate(RootNamespace, sources, languageMode, flagOverrides)
            );
            generated.Add(new TaskItem(entryPoint));
        }

        foreach (ITaskItem contractItem in Contracts)
        {
            var contractPath = contractItem.GetMetadata("FullPath");
            var moduleName = Path.GetFileNameWithoutExtension(contractPath);

            if (!TryFindSource(moduleName, out var sourcePath))
            {
                Log.LogError(
                    subcategory: null,
                    errorCode: CodeGenDiagnosticIds.MissingModuleDeclaration,
                    helpKeyword: null,
                    file: contractPath,
                    lineNumber: 0,
                    columnNumber: 0,
                    endLineNumber: 0,
                    endColumnNumber: 0,
                    message: $"No PrologCompile item named '{moduleName}.pl' matches this contract."
                );

                continue;
            }

            ContractReadResult result = ContractReader.Read(File.ReadAllText(contractPath), RootNamespace, contractPath);
            ReportDiagnostics(result.Diagnostics, contractPath);

            if (!result.Success)
            {
                continue;
            }

            var facade = FacadeGenerator.Generate(
                result.Contract!,
                File.ReadAllText(sourcePath),
                Path.GetFileName(sourcePath),
                languageMode,
                flagOverrides
            );

            var target = Path.Combine(OutputPath, $"{result.Contract!.ClrTypeName}Module.g.cs");
            WriteIfChanged(target, facade);
            generated.Add(new TaskItem(target));

            Log.LogMessage(MessageImportance.Normal, $"Generated {target} from {Path.GetFileName(contractPath)}.");
        }

        RemoveStaleOutputs(generated);

        GeneratedFiles = [.. generated];
        return !Log.HasLoggedErrors;
    }

    /// <summary>
    /// Deletes generated files a previous build wrote but this one did not, so a renamed or removed
    /// contract cannot leave its old facade behind to be compiled. The directory is task-owned,
    /// which is what makes deleting unrecognised <c>.g.cs</c> files safe.
    /// </summary>
    private void RemoveStaleOutputs(List<ITaskItem> generated)
    {
        HashSet<string> expected = new(StringComparer.OrdinalIgnoreCase);
        foreach (ITaskItem item in generated)
        {
            expected.Add(Path.GetFullPath(item.ItemSpec));
        }

        foreach (var file in Directory.EnumerateFiles(OutputPath, "*.g.cs", SearchOption.AllDirectories))
        {
            var path = Path.GetFullPath(file);
            if (expected.Contains(path))
            {
                continue;
            }

            File.Delete(path);
            Log.LogMessage(MessageImportance.Normal, $"Deleted stale generated file {path}.");
        }
    }

    private bool TryFindSource(string moduleName, out string path)
    {
        foreach (ITaskItem source in Sources)
        {
            var candidate = source.GetMetadata("FullPath");
            if (string.Equals(Path.GetFileNameWithoutExtension(candidate), moduleName, StringComparison.Ordinal))
            {
                path = candidate;
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    private void ReportDiagnostics(IReadOnlyList<Diagnostic> diagnostics, string defaultFile)
    {
        foreach (Diagnostic diagnostic in diagnostics)
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
