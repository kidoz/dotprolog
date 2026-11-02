using System.Text.Json.Nodes;
using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;

namespace DotProlog.LanguageServer;

// Each instance is confined to one analysis worker. Forks share only completed, read-only results.
internal sealed class DocumentWorkspace(PrologLanguageMode mode, ProjectConfiguration? project = null)
{
    private readonly Dictionary<string, DocumentSnapshot> _documents = new(DocumentSnapshot.KeyComparer);
    private Dictionary<string, WorkspaceAnalysis> _analyses = new(DocumentSnapshot.KeyComparer);
    private Dictionary<string, DiagnosticPublication> _published = new(DocumentSnapshot.KeyComparer);

    private Dictionary<string, SourceOutline> _outlines = new(DocumentSnapshot.KeyComparer);
    private CancellationToken _analysisCancellation;

    internal JsonArray Symbols(string uri, bool hierarchical) =>
        DocumentSymbols.Convert(uri, _outlines.GetValueOrDefault(DocumentSnapshot.Key(uri)), hierarchical);

    internal JsonArray Completion(string uri, int line, int character) =>
        DocumentCompletion.Convert(
            _outlines.GetValueOrDefault(DocumentSnapshot.Key(uri)),
            DocumentSnapshot.Key(uri),
            line,
            character
        );

    internal JsonObject? Hover(string uri, int line, int character, bool markdown) =>
        DocumentHover.Convert(_outlines.GetValueOrDefault(DocumentSnapshot.Key(uri)), line, character, markdown);

    internal DocumentWorkspace Fork()
    {
        var copy = new DocumentWorkspace(mode, project);
        foreach (var item in _documents)
            copy._documents.Add(item.Key, item.Value);
        foreach (var item in _analyses)
            copy._analyses.Add(item.Key, item.Value);
        copy._published = new Dictionary<string, DiagnosticPublication>(_published, DocumentSnapshot.KeyComparer);
        copy._outlines = new Dictionary<string, SourceOutline>(_outlines, DocumentSnapshot.KeyComparer);
        return copy;
    }

    internal IReadOnlyList<DiagnosticPublication> AnalyzeSnapshot(
        IReadOnlyList<DocumentSnapshot> documents,
        IReadOnlyList<string> diskChanges,
        ProjectConfiguration? configuration,
        CancellationToken cancellationToken
    )
    {
        _analysisCancellation = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<string, DocumentSnapshot> next = new(DocumentSnapshot.KeyComparer);
        foreach (var document in documents)
            next[DocumentSnapshot.Key(document.Uri)] = document;
        HashSet<string> changed = new(DocumentSnapshot.KeyComparer);
        foreach (var item in _documents)
            if (!next.TryGetValue(item.Key, out var document) || document != item.Value)
                changed.Add(item.Key);
        foreach (var item in next)
            if (!_documents.TryGetValue(item.Key, out var document) || document != item.Value)
                changed.Add(item.Key);
        foreach (string uri in diskChanges)
            if (new Uri(uri).IsFile && !next.ContainsKey(DocumentSnapshot.Key(uri)))
                changed.Add(DocumentSnapshot.Key(uri));
        foreach (string key in _documents.Keys)
            if (!next.ContainsKey(key))
                _analyses.Remove(key);
        _documents.Clear();
        foreach (var item in next)
            _documents.Add(item.Key, item.Value);
        if (configuration is not null && (project is null || !project.HasSameAnalysisSettings(configuration)))
        {
            project = configuration;
            _analyses.Clear();
            changed.UnionWith(next.Keys);
        }
        Reanalyze(changed);
        cancellationToken.ThrowIfCancellationRequested();
        return Publications();
    }

    internal IReadOnlyList<DiagnosticPublication> StartProject()
    {
        if (project is null || _analyses.ContainsKey(project.ProjectPath))
            return [];
        Reanalyze([]);
        return Publications();
    }

    internal IReadOnlyList<DiagnosticPublication> ReplaceProject(ProjectConfiguration configuration)
    {
        // Stage a complete analysis before publishing or discarding the previous configuration.
        ProjectConfiguration? previousProject = project;
        Dictionary<string, WorkspaceAnalysis> previousAnalyses = _analyses;
        project = configuration;
        _analyses = new Dictionary<string, WorkspaceAnalysis>(DocumentSnapshot.KeyComparer);
        try
        {
            Reanalyze(new HashSet<string>(_documents.Keys, DocumentSnapshot.KeyComparer));
            return Publications();
        }
        catch
        {
            project = previousProject;
            _analyses = previousAnalyses;
            throw;
        }
    }

    internal bool IsNewer(string uri, int version) =>
        _documents.TryGetValue(DocumentSnapshot.Key(uri), out var document) && version > document.Version;

    internal IReadOnlyList<DiagnosticPublication> Update(DocumentSnapshot document)
    {
        string key = DocumentSnapshot.Key(document.Uri);
        _documents[key] = document;
        Reanalyze(new HashSet<string>(DocumentSnapshot.KeyComparer) { key });
        return Publications();
    }

    internal IReadOnlyList<DiagnosticPublication> Close(string uri)
    {
        string key = DocumentSnapshot.Key(uri);
        if (!_documents.Remove(key))
            return [];
        _analyses.Remove(key);
        Reanalyze(new HashSet<string>(DocumentSnapshot.KeyComparer) { key });
        return Publications();
    }

    internal IReadOnlyList<DiagnosticPublication> DiskChanged(IEnumerable<string> uris)
    {
        HashSet<string> changed = new(DocumentSnapshot.KeyComparer);
        foreach (string uri in uris)
        {
            string key = DocumentSnapshot.Key(uri);
            // An open document is authoritative even when its on-disk file changes or disappears.
            if (new Uri(uri).IsFile && !_documents.ContainsKey(key))
                changed.Add(key);
        }
        if (changed.Count == 0)
            return [];
        Reanalyze(changed);
        return Publications();
    }

    private void Reanalyze(HashSet<string> changed)
    {
        string[] affected =
        [
            .. _documents.Keys.Where(key =>
                changed.Contains(key) || (_analyses.TryGetValue(key, out var analysis) && analysis.Dependencies.Overlaps(changed))
            ),
        ];
        // Cache disk reads within this update so shared includes have the same source text.
        Dictionary<string, string> disk = new(DocumentSnapshot.KeyComparer);
        foreach (string key in affected)
        {
            _analysisCancellation.ThrowIfCancellationRequested();
            _analyses[key] = Analyze(key, _documents[key], disk);
        }
        if (
            project is not null
            && (!_analyses.TryGetValue(project.ProjectPath, out var previous) || previous.Dependencies.Overlaps(changed))
        )
            _analyses[project.ProjectPath] = AnalyzeProject(project, disk);
    }

    private WorkspaceAnalysis Analyze(string key, DocumentSnapshot document, Dictionary<string, string> disk)
    {
        HashSet<string> dependencies = new(DocumentSnapshot.KeyComparer);
        Dictionary<string, JsonArray> diagnostics = new(DocumentSnapshot.KeyComparer);
        if (project is not null && project.Sources.Contains(key, DocumentSnapshot.KeyComparer))
            return new WorkspaceAnalysis(dependencies, diagnostics, new(DocumentSnapshot.KeyComparer));
        if (!new Uri(document.Uri).IsFile)
        {
            var analysis = DocumentDiagnostics.AnalyzeDocument(document.Text, document.Uri, mode, _analysisCancellation);
            diagnostics[key] = analysis.Diagnostics;
            return new WorkspaceAnalysis(
                dependencies,
                diagnostics,
                new Dictionary<string, SourceOutline>(DocumentSnapshot.KeyComparer) { [key] = analysis.Outline }
            );
        }

        Dictionary<string, string> sources = new(DocumentSnapshot.KeyComparer) { [key] = document.Text };
        PrologLintSourceLoadResult loaded = PrologLintSourceLoader.Load(
            key,
            document.Text,
            new BytecodeProgram(mode),
            ReadSource,
            _analysisCancellation
        );
        _analysisCancellation.ThrowIfCancellationRequested();
        Diagnostic[] findings = [.. loaded.Diagnostics.Concat(PrologLinter.AnalyzeProgram([loaded.Source]))];
        foreach ((string path, string text) in sources)
        {
            diagnostics[path] = DocumentDiagnostics.Convert(
                text,
                findings.Where(item => DocumentSnapshot.KeyComparer.Equals(item.FileName, path))
            );
        }
        return new WorkspaceAnalysis(dependencies, diagnostics, Outlines([loaded.Source], sources, mode));

        string ReadSource(string path)
        {
            _analysisCancellation.ThrowIfCancellationRequested();
            // Record failed reads as dependencies too: opening a missing include must repair its parent.
            dependencies.Add(path);
            string text;
            if (_documents.TryGetValue(path, out var overlay))
                text = overlay.Text;
            else if (!disk.TryGetValue(path, out text!))
            {
                text = File.ReadAllText(path);
                disk[path] = text;
            }
            sources[path] = text;
            return text;
        }
    }

    private WorkspaceAnalysis AnalyzeProject(ProjectConfiguration configuration, Dictionary<string, string> disk)
    {
        HashSet<string> dependencies = new(configuration.Sources, DocumentSnapshot.KeyComparer);
        Dictionary<string, string> sources = new(DocumentSnapshot.KeyComparer);
        List<PrologLintSource> units = [];
        List<Diagnostic> findings = [];
        foreach (string path in configuration.Sources)
        {
            _analysisCancellation.ThrowIfCancellationRequested();
            try
            {
                var loaded = PrologLintSourceLoader.Load(
                    path,
                    ReadSource(path),
                    new BytecodeProgram(configuration.Mode, configuration.Flags),
                    ReadSource,
                    _analysisCancellation
                );
                units.Add(loaded.Source);
                findings.AddRange(loaded.Diagnostics);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                sources[path] = string.Empty;
                findings.Add(
                    new Diagnostic(
                        LintDiagnosticIds.UnresolvedLintSource,
                        DiagnosticSeverity.Warning,
                        $"Cannot read project source: {exception.Message}",
                        SourceSpan.None,
                        path
                    )
                );
            }
        }
        _analysisCancellation.ThrowIfCancellationRequested();
        findings.AddRange(PrologLinter.AnalyzeProgram(units));
        Dictionary<string, JsonArray> diagnostics = new(DocumentSnapshot.KeyComparer);
        foreach ((string path, string text) in sources)
            diagnostics[path] = DocumentDiagnostics.Convert(
                text,
                findings.Where(item => DocumentSnapshot.KeyComparer.Equals(item.FileName, path))
            );
        return new WorkspaceAnalysis(dependencies, diagnostics, Outlines(units, sources, configuration.Mode));

        string ReadSource(string path)
        {
            _analysisCancellation.ThrowIfCancellationRequested();
            dependencies.Add(path);
            string text;
            if (_documents.TryGetValue(path, out var overlay))
                text = overlay.Text;
            else if (!disk.TryGetValue(path, out text!))
            {
                text = File.ReadAllText(path);
                disk[path] = text;
            }
            sources[path] = text;
            return text;
        }
    }

    private Dictionary<string, SourceOutline> Outlines(
        IReadOnlyList<PrologLintSource> units,
        Dictionary<string, string> sources,
        PrologLanguageMode languageMode
    )
    {
        var symbols = PrologSourceSymbols.Analyze(units, _analysisCancellation);
        var hovers = PrologBuiltinHovers.Analyze(units, languageMode, _analysisCancellation);
        var completions = PrologCompletionIndex.Create(units, languageMode, _analysisCancellation);
        return sources.ToDictionary(
            item => item.Key,
            item => new SourceOutline(
                item.Value,
                symbols.Where(symbol => DocumentSnapshot.KeyComparer.Equals(symbol.FileName, item.Key)).ToArray(),
                hovers.Where(hover => DocumentSnapshot.KeyComparer.Equals(hover.FileName, item.Key)).ToArray(),
                PrologCompletionText.Create(item.Value, _analysisCancellation),
                [completions]
            ),
            DocumentSnapshot.KeyComparer
        );
    }

    private List<DiagnosticPublication> Publications()
    {
        _outlines = new Dictionary<string, SourceOutline>(DocumentSnapshot.KeyComparer);
        Dictionary<string, JsonArray> merged = new(DocumentSnapshot.KeyComparer);
        HashSet<string> covered = new(DocumentSnapshot.KeyComparer);
        string[] keys = [.. _analyses.Keys.Order(StringComparer.Ordinal)];
        // Analyze included buffers in their including context, not again with default operators.
        foreach (
            string key in keys.Where(key =>
                !_analyses.Any(other =>
                    !DocumentSnapshot.KeyComparer.Equals(other.Key, key) && other.Value.Dependencies.Contains(key)
                )
            )
        )
            AddRoot(key);
        // A cycle has no parentless root. Choose a deterministic representative for each remaining component.
        foreach (string key in keys)
            if (!covered.Contains(key))
                AddRoot(key);

        Dictionary<string, DiagnosticPublication> next = new(DocumentSnapshot.KeyComparer);
        foreach ((string key, JsonArray diagnostics) in merged)
        {
            _documents.TryGetValue(key, out var document);
            next[key] = new DiagnosticPublication(document?.Uri ?? new Uri(key).AbsoluteUri, document?.Version, diagnostics);
        }
        List<DiagnosticPublication> changes = [];
        foreach ((string key, DiagnosticPublication publication) in next)
        {
            if (
                !_published.TryGetValue(key, out var previous)
                || previous.Uri != publication.Uri
                || previous.Version != publication.Version
                || !JsonNode.DeepEquals(previous.Diagnostics, publication.Diagnostics)
            )
                changes.Add(publication);
        }
        foreach ((string key, DiagnosticPublication previous) in _published)
            if (!next.ContainsKey(key))
                changes.Add(new DiagnosticPublication(previous.Uri, null, []));
        _published = next;
        return changes;

        void AddRoot(string key)
        {
            covered.Add(key);
            WorkspaceAnalysis analysis = _analyses[key];
            covered.UnionWith(analysis.Dependencies);
            foreach (var (path, outline) in analysis.Outlines)
            {
                _outlines[path] = _outlines.TryGetValue(path, out var previous)
                    ? new SourceOutline(
                        outline.Text,
                        previous.Symbols.Concat(outline.Symbols).Distinct().ToArray(),
                        previous.Hovers.Concat(outline.Hovers).Distinct().ToArray(),
                        outline.CompletionText,
                        previous.Completions.Concat(outline.Completions).Distinct().ToArray()
                    )
                    : outline;
            }
            foreach ((string path, JsonArray diagnostics) in analysis.Diagnostics)
            {
                if (!merged.TryGetValue(path, out var combined))
                {
                    combined = [];
                    merged[path] = combined;
                }
                foreach (JsonNode? diagnostic in diagnostics)
                    if (!combined.Any(existing => JsonNode.DeepEquals(existing, diagnostic)))
                        combined.Add(diagnostic?.DeepClone());
            }
        }
    }
}
