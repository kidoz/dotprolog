using System.Text.Json.Nodes;

namespace DotProlog.LanguageServer;

internal sealed record WorkspaceAnalysis(
    HashSet<string> Dependencies,
    Dictionary<string, JsonArray> Diagnostics,
    Dictionary<string, SourceOutline> Outlines
);
