using System.Text.Json.Nodes;
using DotProlog.Runtime;

namespace DotProlog.LanguageServer;

internal sealed record ProjectConfiguration(
    string ProjectPath,
    PrologLanguageMode Mode,
    PrologFlagOverrides Flags,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Contracts
)
{
    internal bool HasSameAnalysisSettings(ProjectConfiguration other) =>
        DocumentSnapshot.KeyComparer.Equals(ProjectPath, other.ProjectPath)
        && Mode == other.Mode
        && Flags == other.Flags
        && Sources.SequenceEqual(other.Sources, DocumentSnapshot.KeyComparer)
        && Contracts.SequenceEqual(other.Contracts, DocumentSnapshot.KeyComparer);

    internal static ProjectConfiguration Parse(string json)
    {
        JsonObject root =
            JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("MSBuild returned no project description.");
        JsonObject properties =
            root["Properties"]?.AsObject() ?? throw new InvalidDataException("MSBuild properties are missing.");
        string Property(string name) => properties[name]?.GetValue<string>() ?? string.Empty;
        if (!PrologLanguageModes.TryParse(Property("DotPrologLanguageMode"), out var mode))
            throw new InvalidDataException("The project must import DotProlog.Sdk and specify a valid DotPrologLanguageMode.");
        if (!PrologFlagOverrides.TryParse(Property("DotPrologFlags"), out var flags, out var error))
            throw new InvalidDataException(error);
        if (mode == PrologLanguageMode.StrictIso && flags.DoubleQuotes == DoubleQuotesMode.String)
            throw new InvalidDataException("double_quotes=string is unavailable in strict-iso mode.");
        if (Property("TargetFrameworks").Length != 0 && Property("TargetFramework").Length == 0)
            throw new InvalidDataException(
                "Multi-target projects require a single TargetFramework for language server evaluation."
            );
        string path = Property("MSBuildProjectFullPath");
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidDataException("MSBuild returned an invalid project path.");
        return new ProjectConfiguration(path, mode, flags, Items("PrologCompile"), Items("PrologContract"));

        string[] Items(string name)
        {
            JsonArray items =
                root["Items"]?[name]?.AsArray() ?? throw new InvalidDataException($"MSBuild item list {name} is missing.");
            List<string> paths = [];
            foreach (JsonNode? item in items)
            {
                string fullPath = item?["FullPath"]?.GetValue<string>() ?? string.Empty;
                if (!Path.IsPathFullyQualified(fullPath))
                    throw new InvalidDataException($"MSBuild returned an invalid {name} path.");
                paths.Add(Path.GetFullPath(fullPath));
            }
            return [.. paths.Distinct(DocumentSnapshot.KeyComparer)];
        }
    }
}
