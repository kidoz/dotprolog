using System.Text.Json.Nodes;
using StreamJsonRpc.Protocol;

namespace DotProlog.LanguageServer;

// The transport owns framing; the server owns the small advertised protocol surface.
internal sealed class LspMessage(JsonNode? body) : JsonRpcMessage
{
    internal JsonNode? Body { get; } = body;
}
