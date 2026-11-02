using System.Text.Json;
using DotProlog.Runtime;

namespace DotProlog.LanguageServer;

internal sealed class AnalysisScheduler : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, DocumentSnapshot> _documents = new(DocumentSnapshot.KeyComparer);
    private readonly HashSet<string> _diskChanges = new(StringComparer.Ordinal);
    private readonly Func<CancellationToken, Task<ProjectConfiguration>>? _reload;
    private readonly Func<IReadOnlyList<DiagnosticPublication>, string?, int, Task> _publish;
    private readonly Dictionary<string, PendingSymbolRequest> _symbolRequests = new(StringComparer.Ordinal);
    private bool _analysisFailed;
    private readonly TimeSpan _debounce;
    private readonly Task _worker;
    private DocumentWorkspace _workspace;
    private ProjectConfiguration? _project;
    private string? _reloadError;
    private CancellationTokenSource? _active;
    private long _generation;
    private bool _stopping;
    private TaskCompletionSource _idle = Completed();

    internal AnalysisScheduler(
        PrologLanguageMode mode,
        ProjectConfiguration? project,
        Func<CancellationToken, Task<ProjectConfiguration>>? reload,
        Func<IReadOnlyList<DiagnosticPublication>, string?, int, Task> publish,
        TimeSpan debounce
    )
    {
        _workspace = new(mode, project);
        _project = project;
        _reload = reload;
        _publish = publish;
        _debounce = debounce;
        _worker = Task.Run(WorkAsync);
    }

    internal Task StartProjectAsync() => ChangeAsync(() => _project is not null);

    internal Task UpdateAsync(DocumentSnapshot document, bool isChange) =>
        ChangeAsync(() =>
        {
            string key = DocumentSnapshot.Key(document.Uri);
            if (isChange && (!_documents.TryGetValue(key, out var previous) || previous.Version >= document.Version))
                return false;
            _documents[key] = document;
            return true;
        });

    internal Task CloseAsync(string uri) => ChangeAsync(() => _documents.Remove(DocumentSnapshot.Key(uri)));

    internal Task DiskChangedAsync(IReadOnlyList<string> uris) =>
        ChangeAsync(() =>
        {
            foreach (string uri in uris)
                if (new Uri(uri).IsFile)
                    _diskChanges.Add(uri);
            return _diskChanges.Count > 0;
        });

    internal async Task RequestSymbolsAsync(PendingSymbolRequest request)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stopping)
                await request.Reply(null, -32800).ConfigureAwait(false);
            else if (_symbolRequests.ContainsKey(request.Id))
                await request.Reply(null, -32600).ConfigureAwait(false);
            else if (_idle.Task.IsCompleted)
                await request
                    .Reply(
                        _analysisFailed ? null : _workspace.Symbols(request.Uri, request.Hierarchical),
                        _analysisFailed ? -32803 : null
                    )
                    .ConfigureAwait(false);
            else
                _symbolRequests.TryAdd(request.Id, request);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task CancelSymbolRequestAsync(string id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_symbolRequests.Remove(id, out var request))
                await request.Reply(null, -32800).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CompleteSymbolRequestsAsync(int? error)
    {
        PendingSymbolRequest[] requests = [.. _symbolRequests.Values];
        _symbolRequests.Clear();
        foreach (var request in requests)
            await request
                .Reply(error is null ? _workspace.Symbols(request.Uri, request.Hierarchical) : null, error)
                .ConfigureAwait(false);
    }

    private async Task ChangeAsync(Func<bool> change)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stopping || !change())
                return;
            _generation++;
            if (_active is not null)
                await _active.CancelAsync().ConfigureAwait(false);
            if (_idle.Task.IsCompleted)
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_wake.CurrentCount == 0)
                _wake.Release();
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task WaitForIdleAsync()
    {
        Task idle;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            idle = _idle.Task;
        }
        finally
        {
            _gate.Release();
        }
        await idle.ConfigureAwait(false);
    }

    internal async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _stopping = true;
            _generation++;
            if (_active is not null)
                await _active.CancelAsync().ConfigureAwait(false);
            await _lifetime.CancelAsync().ConfigureAwait(false);
            await CompleteSymbolRequestsAsync(-32800).ConfigureAwait(false);
            _idle.TrySetResult();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WorkAsync()
    {
        try
        {
            while (true)
            {
                await _wake.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                long generation;
                CancellationTokenSource active;
                DocumentSnapshot[] documents;
                string[] disk;
                DocumentWorkspace candidate;
                ProjectConfiguration? project;
                string? previousError;
                await _gate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                try
                {
                    generation = _generation;
                    active = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                    _active = active;
                    documents = [.. _documents.Values];
                    disk = [.. _diskChanges];
                    candidate = _workspace.Fork();
                    project = _project;
                    previousError = _reloadError;
                }
                finally
                {
                    _gate.Release();
                }
                try
                {
                    await Task.Delay(_debounce, active.Token).ConfigureAwait(false);
                    string? error = previousError;
                    string? log = null;
                    var logType = 3;
                    if (disk.Length > 0 && project is not null && _reload is not null)
                    {
                        try
                        {
                            ProjectConfiguration updated = await _reload(active.Token).ConfigureAwait(false);
                            active.Token.ThrowIfCancellationRequested();
                            if (!DocumentSnapshot.KeyComparer.Equals(updated.ProjectPath, project.ProjectPath))
                                throw new InvalidDataException("Reevaluation returned a different project.");
                            project = updated;
                            if (previousError is not null)
                                log = "Project evaluation recovered; current settings are now active.";
                            error = null;
                        }
                        catch (Exception exception)
                            when (exception
                                    is IOException
                                        or InvalidDataException
                                        or JsonException
                                        or System.ComponentModel.Win32Exception
                                        or ArgumentException
                                        or InvalidOperationException
                            )
                        {
                            error = exception.Message;
                            logType = 1;
                            if (error != previousError)
                                log = $"Project reevaluation failed; retaining the last valid settings. {error}";
                        }
                    }
                    var publications = candidate.AnalyzeSnapshot(documents, disk, project, active.Token);
                    active.Token.ThrowIfCancellationRequested();
                    await _gate.WaitAsync(active.Token).ConfigureAwait(false);
                    try
                    {
                        if (_stopping || generation != _generation)
                            continue;
                        // Publication and input acceptance share this gate: an obsolete generation cannot publish.
                        await _publish(publications, log, logType).ConfigureAwait(false);
                        _workspace = candidate;
                        _project = project;
                        _reloadError = error;
                        _diskChanges.Clear();
                        _analysisFailed = false;
                        await CompleteSymbolRequestsAsync(null).ConfigureAwait(false);
                        _idle.TrySetResult();
                    }
                    finally
                    {
                        _gate.Release();
                    }
                }
                catch (OperationCanceledException) when (active.IsCancellationRequested) { }
                catch (Exception exception)
                    when (exception
                            is IOException
                                or InvalidDataException
                                or JsonException
                                or ArgumentException
                                or InvalidOperationException
                                or UnauthorizedAccessException
                    )
                {
                    await _gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (!_stopping && generation == _generation)
                        {
                            await _publish([], $"Analysis failed; retaining previous results. {exception.Message}", 1)
                                .ConfigureAwait(false);
                            _analysisFailed = true;
                            await CompleteSymbolRequestsAsync(-32803).ConfigureAwait(false);
                            _idle.TrySetResult();
                        }
                    }
                    finally
                    {
                        _gate.Release();
                    }
                }
                finally
                {
                    await _gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (ReferenceEquals(_active, active))
                            _active = null;
                    }
                    finally
                    {
                        _gate.Release();
                    }
                    active.Dispose();
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        try
        {
            await _worker.ConfigureAwait(false);
        }
        finally
        {
            _active?.Dispose();
            _gate.Dispose();
            _wake.Dispose();
            _lifetime.Dispose();
        }
    }

    private static TaskCompletionSource Completed()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}
