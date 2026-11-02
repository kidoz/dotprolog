using System.Text.Json.Nodes;

namespace DotProlog.LanguageServer;

internal sealed record PendingAnalysisRequest(
    string Id,
    Func<DocumentWorkspace, JsonNode?> Read,
    Func<JsonNode?, int?, Task> Reply,
    bool InvalidateOnChange = false
);
