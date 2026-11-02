using System.Text.Json.Nodes;

namespace DotProlog.LanguageServer;

internal sealed record PendingSymbolRequest(string Id, string Uri, bool Hierarchical, Func<JsonArray?, int?, Task> Reply);
