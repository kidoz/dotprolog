using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace DotProlog.LanguageServer;

internal sealed class LspMessageFormatter : IJsonRpcMessageTextFormatter
{
    public Encoding Encoding
    {
        get => Encoding.UTF8;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.CodePage != Encoding.UTF8.CodePage)
                throw new ArgumentException("LSP requires UTF-8.", nameof(value));
        }
    }

    public JsonRpcMessage Deserialize(ReadOnlySequence<byte> contentBuffer, Encoding encoding)
    {
        Encoding = encoding;
        return Deserialize(contentBuffer);
    }

    public JsonRpcMessage Deserialize(ReadOnlySequence<byte> contentBuffer)
    {
        var reader = new Utf8JsonReader(contentBuffer);
        JsonNode? body = JsonNode.Parse(ref reader);
        if (reader.Read())
            throw new JsonException("Unexpected trailing JSON.");
        return new LspMessage(body);
    }

    public object GetJsonText(JsonRpcMessage message) => ((LspMessage)message).Body?.ToJsonString() ?? "null";

    public void Serialize(IBufferWriter<byte> contentBuffer, JsonRpcMessage message)
    {
        using var writer = new Utf8JsonWriter(contentBuffer);
        ((LspMessage)message).Body!.WriteTo(writer);
    }
}
