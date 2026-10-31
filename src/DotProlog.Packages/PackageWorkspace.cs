using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotProlog.Packages;

/// <summary>Restores portable Prolog source packages without starting a Prolog engine.</summary>
public sealed partial class PackageWorkspace
{
    private readonly string _root;
    private readonly string _cache;
    private string ManifestPath => Path.Combine(_root, "propack.json");
    private string LockPath => Path.Combine(_root, "propack.lock.json");

    /// <summary>Creates a workspace with an optional isolated cache directory.</summary>
    public PackageWorkspace(string directory, string? cacheDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _root = Path.GetFullPath(directory);
        _cache = Path.GetFullPath(
            cacheDirectory
                ?? Environment.GetEnvironmentVariable("PROPACK_CACHE")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "propack", "cache-v1")
        );
        // GetRelativePath applies the platform's path comparison and normalizes directory boundaries.
        // Only parent-relative paths and paths on another file-system root are outside the project.
        var relativeCache = Path.GetRelativePath(_root, _cache);
        if (
            !Path.IsPathRooted(relativeCache)
            && relativeCache != ".."
            && !relativeCache.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        )
            throw new ArgumentException("The package cache must be outside the project.", nameof(cacheDirectory));
    }

    /// <summary>Restores dependencies, preserving an existing lock unless update is requested.</summary>
    public void Restore(bool locked = false, bool update = false)
    {
        if (locked && update)
            throw new ArgumentException("Locked restore cannot update dependencies.");
        var manifest = ReadManifest(_root);
        Directory.CreateDirectory(_cache);
        if (!update && File.Exists(LockPath))
        {
            ValidateLock(manifest, acquire: true);
            return;
        }
        if (locked)
            throw new InvalidDataException("Missing propack.lock.json; run restore first.");
        var catalog = ReadCatalog(manifest);
        Dictionary<string, Candidate> selected = new(StringComparer.Ordinal);
        Dictionary<string, Candidate> acquired = new(StringComparer.Ordinal);
        Dictionary<string, string> unavailable = new(StringComparer.Ordinal);
        var pending = manifest.Dependencies.Select(p => new Request(p.Key, p.Value, _root, manifest.Name)).ToList();
        var failure = "No compatible dependency graph.";
        var steps = 0;
        if (!Solve(pending, selected))
            throw new InvalidDataException(
                string.Join('\n', new[] { failure }.Concat(unavailable.Values).Distinct(StringComparer.Ordinal))
            );
        var result = new PackageLock
        {
            ManifestHash = PackageFiles.HashFile(ManifestPath),
            Engine = manifest.Engine,
            Packages = selected.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value.Locked).ToList(),
        };
        ValidateGraph(manifest, result);
        PackageFiles.WriteAtomic(LockPath, JsonSerializer.Serialize(result, PackageJsonContext.Default.PackageLock) + "\n");

        bool Solve(List<Request> requests, Dictionary<string, Candidate> graph)
        {
            if (++steps > 10000 || graph.Count > 256)
                throw new InvalidDataException("Dependency resolution exceeds 10000 steps or 256 packages.");
            if (requests.Count == 0)
                return true;
            var request = requests[0];
            var remaining = requests.Skip(1).ToList();
            ValidateName(request.Name);
            if (graph.TryGetValue(request.Name, out var existing))
            {
                if (Satisfies(existing.Locked, request))
                    return Solve(remaining, graph);
                failure =
                    $"Dependency conflict: {request.Parent} requires {request.Name} {request.Source.Version}, but selected {existing.Locked.Version} from another requirement/source.";
                return false;
            }
            var explicitSource = HasSource(request.Source);
            var candidates = explicitSource
                ? new List<PackageSource> { Normalize(request.Source, request.BaseDirectory) }
                : catalog
                    .GetValueOrDefault(request.Name, [])
                    .Where(s => PackageVersions.Matches(s.Version, request.Source.Version))
                    .OrderByDescending(s => PackageVersions.Parse(s.Version))
                    .ToList();
            if (candidates.Count == 0)
                failure = $"No candidate for {request.Name} {request.Source.Version}, required by {request.Parent}.";
            foreach (var source in candidates)
            {
                var key = JsonSerializer.Serialize(source, PackageJsonContext.Default.PackageSource);
                if (!explicitSource && unavailable.TryGetValue(key, out var transportFailure))
                {
                    failure = transportFailure;
                    continue;
                }
                if (!acquired.TryGetValue(key, out var candidate))
                {
                    (string directory, PackageSource pinned) fetched;
                    try
                    {
                        fetched = PackageFiles.Acquire(source, _cache);
                    }
                    catch (Exception exception)
                        when (!explicitSource
                            && exception
                                is IOException
                                    or HttpRequestException
                                    or OperationCanceledException
                                    or System.ComponentModel.Win32Exception
                                    or UnauthorizedAccessException
                        )
                    {
                        failure =
                            $"Cannot acquire {request.Name} {source.Version}, required by {request.Parent}: {exception.Message}";
                        unavailable.Add(key, failure);
                        continue;
                    }
                    var (directory, pinned) = fetched;
                    var package = ReadManifest(directory);
                    ValidatePackageFiles(directory, package);
                    if (package.Name != request.Name)
                        throw new InvalidDataException($"Expected package {request.Name}, source declares {package.Name}.");
                    if (!explicitSource && package.Version != source.Version)
                        throw new InvalidDataException($"Catalog version disagrees with {package.Name} manifest.");
                    candidate = new Candidate(
                        package,
                        new LockedPackage
                        {
                            Name = package.Name,
                            Version = package.Version,
                            Source = Portable(pinned),
                            RequestedRevision = source.Revision,
                            Hash = PackageFiles.HashTree(directory, requireSnapshot: true),
                        },
                        source.Path ?? directory
                    );
                    acquired.Add(key, candidate);
                }
                if (!PackageVersions.Matches(candidate.Manifest.Version, request.Source.Version))
                {
                    failure =
                        $"Dependency conflict: {request.Parent} requires {request.Name} {request.Source.Version}, source contains {candidate.Manifest.Version}.";
                    continue;
                }
                if (candidate.Manifest.Engines.Count > 0 && !candidate.Manifest.Engines.ContainsKey(manifest.Engine))
                {
                    failure = $"{request.Name} does not declare support for {manifest.Engine}.";
                    continue;
                }
                graph.Add(request.Name, candidate);
                var next = candidate
                    .Manifest.Dependencies.Select(p => new Request(p.Key, p.Value, candidate.BaseDirectory, request.Name))
                    .Concat(remaining)
                    .ToList();
                if (Solve(next, graph))
                    return true;
                graph.Remove(request.Name);
            }
            return false;
        }
    }

    /// <summary>Validates the lock and cache without fetching, and returns declared library aliases.</summary>
    public IReadOnlyDictionary<string, string> GetLibraries(string? engine = null, string? engineVersion = null)
    {
        var manifest = ReadManifest(_root);
        var packages = ValidateLock(manifest, acquire: false);
        Dictionary<string, string> libraries = new(StringComparer.Ordinal);
        CheckEngine(manifest, engine ?? manifest.Engine, engineVersion);
        AddModules(manifest, _root, libraries);
        foreach (var package in packages.Packages)
        {
            var directory = CachePath(package.Hash);
            var packageManifest = ReadManifest(directory);
            CheckEngine(packageManifest, engine ?? manifest.Engine, engineVersion);
            AddModules(packageManifest, directory, libraries);
        }
        return libraries;
    }

    /// <summary>Returns a deterministic dependency listing after verifying its lock and contents.</summary>
    public string Describe()
    {
        var root = ReadManifest(_root);
        var locked = ValidateLock(root, acquire: false);
        return string.Join(
                '\n',
                new[] { root.Name + " " + root.Version }.Concat(
                    locked.Packages.Select(p =>
                        $"  {p.Name} {p.Version} [{p.Hash[..12]}] -> {string.Join(", ", ReadManifest(CachePath(p.Hash)).Dependencies.Keys.Order(StringComparer.Ordinal))}"
                    )
                )
            ) + "\n";
    }

    internal string DefaultEngine => ReadManifest(_root).Engine;

    private PackageLock ValidateLock(PackageManifest manifest, bool acquire)
    {
        if (!File.Exists(LockPath))
            throw new InvalidDataException("Missing propack.lock.json; run restore first.");
        var locked =
            JsonSerializer.Deserialize(PackageFiles.ReadText(LockPath), PackageJsonContext.Default.PackageLock)
            ?? throw new InvalidDataException("Empty lockfile.");
        if (locked.Schema != 1 || locked.ManifestHash != PackageFiles.HashFile(ManifestPath) || locked.Engine != manifest.Engine)
            throw new InvalidDataException("Manifest changed or unsupported lock schema; run update explicitly.");
        foreach (var package in locked.Packages)
        {
            if (package is null)
                throw new InvalidDataException("Null package in lockfile.");
            var directory = CachePath(package.Hash);
            var source = Normalize(package.Source, _root);
            if (source.Path is not null && PackageFiles.HashTree(source.Path) != package.Hash)
                throw new InvalidDataException($"Local package {package.Name} changed; run update explicitly.");
            if (!Directory.Exists(directory) && acquire)
            {
                var fetched = PackageFiles.Acquire(source, _cache);
                if (PackageFiles.HashTree(fetched.Directory, requireSnapshot: true) != package.Hash)
                    throw new InvalidDataException($"Content hash mismatch for {package.Name}.");
            }
            if (!Directory.Exists(directory))
                throw new InvalidDataException($"Missing cached package {package.Name}; run restore --locked.");
            if (PackageFiles.HashTree(directory, requireSnapshot: true) != package.Hash)
                throw new InvalidDataException($"Corrupt cached package {package.Name}.");
        }
        ValidateGraph(manifest, locked);
        return locked;
    }

    private void ValidateGraph(PackageManifest root, PackageLock locked)
    {
        Dictionary<string, LockedPackage> graph = new(StringComparer.Ordinal);
        Dictionary<string, string> modules = new(StringComparer.Ordinal);
        AddModules(root, _root, modules);
        foreach (var package in locked.Packages)
        {
            if (!graph.TryAdd(package.Name, package))
                throw new InvalidDataException($"Duplicate locked package {package.Name}.");
            var manifest = ReadManifest(CachePath(package.Hash));
            ValidatePackageFiles(CachePath(package.Hash), manifest);
            if (manifest.Name != package.Name || manifest.Version != package.Version)
                throw new InvalidDataException($"Locked identity mismatch: {package.Name}.");
            CheckEngine(manifest, root.Engine, null);
            AddModules(manifest, CachePath(package.Hash), modules);
        }
        HashSet<string> visited = new(StringComparer.Ordinal);
        HashSet<string> active = new(StringComparer.Ordinal);
        Visit(root, _root);
        if (visited.Count != graph.Count)
            throw new InvalidDataException("Lockfile contains unreachable packages.");
        void Visit(PackageManifest manifest, string baseDirectory)
        {
            if (!active.Add(manifest.Name))
                throw new InvalidDataException($"Dependency cycle at {manifest.Name}.");
            foreach (var (name, source) in manifest.Dependencies)
            {
                if (
                    !graph.TryGetValue(name, out var child)
                    || !Satisfies(child, new Request(name, source, baseDirectory, manifest.Name))
                )
                    throw new InvalidDataException($"Dependency conflict: {manifest.Name} requires {name} {source.Version}.");
                if (active.Contains(name))
                    throw new InvalidDataException($"Dependency cycle at {name}.");
                if (visited.Add(name))
                    Visit(ReadManifest(CachePath(child.Hash)), Normalize(child.Source, _root).Path ?? CachePath(child.Hash));
            }
            active.Remove(manifest.Name);
        }
    }

    private bool Satisfies(LockedPackage package, Request request)
    {
        if (!PackageVersions.Matches(package.Version, request.Source.Version))
            return false;
        if (!HasSource(request.Source))
            return true;
        var expected = Normalize(request.Source, request.BaseDirectory);
        var actual = Normalize(package.Source, _root);
        return expected.Path == actual.Path
            && expected.Git == actual.Git
            && expected.Archive == actual.Archive
            && (expected.Sha256 is null || string.Equals(expected.Sha256, actual.Sha256, StringComparison.OrdinalIgnoreCase))
            && expected.Revision == package.RequestedRevision;
    }

    private Dictionary<string, List<PackageSource>> ReadCatalog(PackageManifest manifest)
    {
        if (manifest.Catalog is null)
            return new(StringComparer.Ordinal);
        var path = Path.GetFullPath(Path.Combine(_root, manifest.Catalog));
        var catalog =
            JsonSerializer.Deserialize(PackageFiles.ReadText(path), PackageJsonContext.Default.DictionaryStringListPackageSource)
            ?? throw new InvalidDataException("Empty catalog.");
        return catalog.ToDictionary(
            p => p.Key,
            p =>
                (p.Value ?? throw new InvalidDataException($"Missing candidates for {p.Key}."))
                    .Select(s =>
                        Normalize(
                            s ?? throw new InvalidDataException($"Missing candidate for {p.Key}."),
                            Path.GetDirectoryName(path)!
                        )
                    )
                    .ToList(),
            StringComparer.Ordinal
        );
    }

    private PackageSource Portable(PackageSource source)
    {
        string? Relative(string? value) =>
            value is null || !Path.IsPathRooted(value) ? value : Path.GetRelativePath(_root, value).Replace('\\', '/');
        return source with { Path = Relative(source.Path), Git = Relative(source.Git), Archive = Relative(source.Archive) };
    }

    private string CachePath(string hash)
    {
        if (!HashPattern().IsMatch(hash))
            throw new InvalidDataException("Invalid package hash in lockfile.");
        return Path.Combine(_cache, hash);
    }

    internal static PackageManifest ReadManifest(string directory)
    {
        var manifest =
            JsonSerializer.Deserialize(
                PackageFiles.ReadText(Path.Combine(directory, "propack.json")),
                PackageJsonContext.Default.PackageManifest
            ) ?? throw new InvalidDataException("Empty package manifest.");
        ValidateManifest(manifest);
        return manifest;
    }

    internal static void ValidateManifest(PackageManifest manifest)
    {
        if (manifest.Schema != 1)
            throw new InvalidDataException("Unsupported manifest schema.");
        ValidateName(manifest.Name);
        PackageVersions.Parse(manifest.Version);
        foreach (var requirement in manifest.Engines.Values)
            PackageVersions.Matches("0.0.0", requirement);
        if (manifest.Engine is not ("dotprolog" or "swi"))
            throw new InvalidDataException("Engine must be dotprolog or swi.");
        foreach (var (name, source) in manifest.Dependencies)
        {
            ValidateName(name);
            if (source is null)
                throw new InvalidDataException($"Missing dependency specification for {name}.");
            PackageFiles.ValidateSourceText(source);
            PackageVersions.Matches("0.0.0", source.Version);
            var sources = (source.Path is null ? 0 : 1) + (source.Git is null ? 0 : 1) + (source.Archive is null ? 0 : 1);
            if (
                sources > 1
                || source.Git is not null && string.IsNullOrWhiteSpace(source.Revision)
                || source.Git is null && source.Revision is not null
            )
                throw new InvalidDataException($"Invalid source for {name}; choose one source and supply a revision for Git.");
        }
    }

    private static void ValidatePackageFiles(string directory, PackageManifest manifest)
    {
        var declared = manifest.Modules.Values.Select(p => PackageFiles.Inside(directory, p)).ToHashSet(StringComparer.Ordinal);
        foreach (var file in PackageFiles.Files(directory).Where(p => p.EndsWith(".pl", StringComparison.OrdinalIgnoreCase)))
            if (!declared.Contains(PackageFiles.Inside(directory, file)))
                throw new InvalidDataException(
                    $"Package {manifest.Name} contains undeclared Prolog source {file}; declare each module explicitly."
                );
    }

    private static void CheckEngine(PackageManifest manifest, string engine, string? version)
    {
        if (manifest.Engines.Count == 0)
            return;
        if (
            !manifest.Engines.TryGetValue(engine, out var requirement)
            || version is not null && !PackageVersions.Matches(version, requirement)
        )
            throw new InvalidDataException($"Package {manifest.Name} does not support {engine} {version}.");
    }

    private static void AddModules(PackageManifest manifest, string directory, Dictionary<string, string> modules)
    {
        foreach (var (name, relative) in manifest.Modules.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!ModuleNamePattern().IsMatch(name))
                throw new InvalidDataException($"Invalid module name '{name}'; use a lowercase identifier.");
            var file = PackageFiles.Inside(directory, relative);
            if (Path.GetRelativePath(directory, file).Replace('\\', '/').Split('/').Any(PackageFiles.IsExcludedName))
                throw new InvalidDataException($"Module {name} uses an excluded package path: {relative}");
            if (Path.GetFileName(file) != name + ".pl" || !File.Exists(file))
                throw new InvalidDataException($"Missing Prolog module {name}: {relative}");
            if (!PackageModuleHeader.Matches(PackageFiles.ReadHeader(directory, relative), name))
                throw new InvalidDataException(
                    $"Module {name} must begin with :- module({name}, ...), after optional comments, within the first 64 KiB."
                );
            if (!modules.TryAdd(name, file))
                throw new InvalidDataException($"Module conflict: '{name}' is exported by more than one package/root.");
        }
    }

    private static bool HasSource(PackageSource source) =>
        source.Path is not null || source.Git is not null || source.Archive is not null;

    private static PackageSource Normalize(PackageSource source, string directory)
    {
        PackageFiles.ValidateSourceText(source);
        if ((source.Path is null ? 0 : 1) + (source.Git is null ? 0 : 1) + (source.Archive is null ? 0 : 1) != 1)
            throw new InvalidDataException("Specify exactly one of path, git, archive.");
        string Location(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "ssh")
                return value;
            if (value.Contains("://", StringComparison.Ordinal))
                throw new InvalidDataException("Remote sources require HTTPS or SSH (Git only).");
            return Path.GetFullPath(Path.Combine(directory, value));
        }
        return source with
        {
            Path = source.Path is null ? null : Path.GetFullPath(Path.Combine(directory, source.Path)),
            Git = source.Git is null ? null : Location(source.Git),
            Archive = source.Archive is null ? null : Location(source.Archive),
        };
    }

    private static void ValidateName(string name)
    {
        if (!PackageNamePattern().IsMatch(name))
            throw new InvalidDataException($"Invalid package name '{name}'; expected owner/name.");
    }

    [GeneratedRegex("^[a-z][a-z0-9_-]*/[a-z][a-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageNamePattern();

    [GeneratedRegex("^[a-z][a-zA-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleNamePattern();

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();

    private sealed record Request(string Name, PackageSource Source, string BaseDirectory, string Parent);

    private sealed record Candidate(PackageManifest Manifest, LockedPackage Locked, string BaseDirectory);
}
