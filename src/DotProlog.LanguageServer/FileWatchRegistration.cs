using System.Text.Json.Nodes;

namespace DotProlog.LanguageServer;

internal sealed class FileWatchRegistration
{
    private const string RequestId = "dotprolog/watch-files";
    private bool _supported;
    private bool _requested;
    private bool _pending;

    internal void Configure(JsonObject capabilities)
    {
        _supported =
            capabilities["workspace"] is JsonObject workspace
            && workspace["didChangeWatchedFiles"] is JsonObject watched
            && watched["dynamicRegistration"] is JsonValue value
            && value.TryGetValue<bool>(out var supported)
            && supported;
    }

    internal JsonObject? CreateRequest()
    {
        if (!_supported || _requested)
            return null;
        _requested = true;
        _pending = true;
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = RequestId,
            ["method"] = "client/registerCapability",
            ["params"] = new JsonObject
            {
                ["registrations"] = new JsonArray(
                    new JsonObject
                    {
                        ["id"] = RequestId,
                        ["method"] = "workspace/didChangeWatchedFiles",
                        ["registerOptions"] = new JsonObject
                        {
                            // Includes may have any extension. The workspace filters notifications to dependencies.
                            ["watchers"] = new JsonArray(new JsonObject { ["globPattern"] = "**/*", ["kind"] = 7 }),
                        },
                    }
                ),
            },
        };
    }

    internal JsonObject? Complete(JsonObject response)
    {
        if (!_pending || response["id"] is not JsonValue id || !id.TryGetValue<string>(out var value) || value != RequestId)
            return null;
        _pending = false;
        if (!response.ContainsKey("error"))
            return null;
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "window/logMessage",
            ["params"] = new JsonObject
            {
                ["type"] = 2,
                ["message"] =
                    "File watch registration was rejected. Disk changes require client-provided workspace/didChangeWatchedFiles notifications; open buffer editing remains available.",
            },
        };
    }
}
