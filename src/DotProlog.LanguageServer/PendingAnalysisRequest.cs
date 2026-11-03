using System.Text.Json.Nodes;

namespace DotProlog.LanguageServer;

internal sealed record PendingAnalysisRequest(
    string Id,
    Func<DocumentWorkspace, JsonNode?> Read,
    Func<JsonNode?, int?, Task> Reply,
    bool InvalidateOnChange = false,
    Func<int, string, Task>? ReplyError = null
)
{
    internal async Task CompleteAsync(DocumentWorkspace workspace, int? error)
    {
        JsonNode? result;
        try
        {
            result = error is null ? Read(workspace) : null;
        }
        catch (AnalysisRequestException exception)
        {
            if (ReplyError is not null)
                await ReplyError(exception.Code, exception.Message).ConfigureAwait(false);
            else
                await Reply(null, exception.Code).ConfigureAwait(false);
            return;
        }
        await Reply(result, error).ConfigureAwait(false);
    }
}
