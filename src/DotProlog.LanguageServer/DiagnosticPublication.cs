using System.Text.Json.Nodes;

namespace DotProlog.LanguageServer;

internal sealed record DiagnosticPublication(string Uri, int? Version, JsonArray Diagnostics);
