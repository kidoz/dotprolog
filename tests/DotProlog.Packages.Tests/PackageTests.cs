using System.IO.Compression;
using System.Text.Json;
using DotProlog.Compiler;
using DotProlog.Runtime;

namespace DotProlog.Packages.Tests;

public sealed class PackageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "propack tests " + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(_directory, "app");
    private string Cache => Path.Combine(_directory, "cache");
    private PackageWorkspace Workspace => new(Root, Cache);

    public PackageTests() => Directory.CreateDirectory(Root);

    public void Dispose() => PackageFiles.DeleteDirectory(_directory);

    [Fact]
    public void Library_aliases_compile_to_direct_IL_and_run_without_source_files()
    {
        PortableGraph();
        Workspace.Restore();
        using var image = new MemoryStream();
        var file = Path.Combine(Root, "main.pl");
        var diagnostics = CodeGen.IL.IlAssemblyEmitter.Emit(
            [(file, File.ReadAllText(file))],
            "PortablePackage",
            image,
            libraryFiles: Workspace.GetLibraries()
        );
        Assert.Empty(diagnostics);
        var context = new System.Runtime.Loader.AssemblyLoadContext(null, isCollectible: true);
        try
        {
            image.Position = 0;
            var install = context
                .LoadFromStream(image)
                .GetType(CodeGen.IL.IlAssemblyEmitter.ProgramTypeName)!
                .GetMethod("Install")!
                .CreateDelegate<Func<PrologEngine, int[]>>();
            Directory.Delete(Cache, true);
            Directory.Delete(Path.Combine(_directory, "words"), true);
            Directory.Delete(Path.Combine(_directory, "greeting"), true);
            using var output = new StringWriter();
            using var engine = new PrologEngine { Output = output };
            foreach (var target in install(engine))
                Assert.Equal(RunResult.Success, engine.Machine.Run(target));
            Assert.Equal("hello\n", output.ToString().Replace("\r", "", StringComparison.Ordinal));
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void Library_imports_are_explicit_engine_scoped_and_remain_modern_only()
    {
        PortableGraph();
        Workspace.Restore();
        using var missing = new PrologEngine();
        Assert.Contains(missing.ConsultText(":- use_module(library(words)).").Diagnostics, d => d.Id == "DPL1009");
        using var invalid = new PrologEngine();
        Assert.Contains(invalid.ConsultText(":- use_module(library(42)).").Diagnostics, d => d.Id == "DPL1008");
        using var strict = new PrologEngine(PrologLanguageMode.StrictIso) { LibraryFiles = Workspace.GetLibraries() };
        Assert.Contains(strict.ConsultText(":- use_module(library(words)).").Diagnostics, d => d.Id == "DPL1018");
        using var configured = new PrologEngine { LibraryFiles = Workspace.GetLibraries() };
        Assert.True(configured.ConsultText(":- use_module(library(words), [word/1]).").Success);
        Assert.True(configured.Query("word(hello)").Prove());
    }

    [Fact]
    public void Minimal_manifest_defaults_and_cli_errors_are_supported()
    {
        File.WriteAllText(Path.Combine(Root, "propack.json"), "{\"name\":\"demo/app\"}");
        Workspace.Restore();
        Assert.Empty(Workspace.GetLibraries());
        File.WriteAllText(Path.Combine(Root, "propack.json"), "{\"name\":\"bad\"}");
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(65, PackageCommands.Execute(["restore", "--project", Root, "--cache", Cache], output, error));
        Assert.Contains("Invalid package name", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Transitive_packages_run_in_DotProlog_and_locked_restore_is_stable()
    {
        PortableGraph();
        Workspace.Restore();
        var original = File.ReadAllText(Path.Combine(Root, "propack.lock.json"));
        Workspace.Restore(locked: true);
        Assert.Equal(original, File.ReadAllText(Path.Combine(Root, "propack.lock.json")));
        using var output = new StringWriter();
        using var engine = new PrologEngine { LibraryFiles = Workspace.GetLibraries("dotprolog", "0.16.0"), Output = output };
        var loaded = engine.ConsultFile(Path.Combine(Root, "main.pl"));
        Assert.True(loaded.Success, string.Join('\n', loaded.Diagnostics));
        Assert.Equal(RunResult.Success, engine.RunPendingGoals());
        Assert.Equal("hello\n", output.ToString().Replace("\r", "", StringComparison.Ordinal));
        Assert.Contains("demo/greeting 1.0.0", Workspace.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Standalone_SWI_adapter_runs_same_transitive_graph_when_requested()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("DOTPROLOG_RUN_SWI_DIFFERENTIAL_TESTS") == "1",
            "Set DOTPROLOG_RUN_SWI_DIFFERENTIAL_TESTS=1 to exercise the installed SWI adapter."
        );
        PortableGraph();
        Workspace.Restore();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = PackageCommands.Execute(
            ["run", "main.pl", "--engine", "swi", "--project", Root, "--cache", Cache],
            output,
            error
        );
        Assert.True(code == 0, error.ToString());
        Assert.Equal("hello\n", output.ToString().Replace("\r", "", StringComparison.Ordinal));
    }

    [Fact]
    public void Changed_manifest_requires_explicit_update()
    {
        PortableGraph();
        Workspace.Restore();
        File.AppendAllText(Path.Combine(Root, "propack.json"), "\n");
        Assert.Contains(
            "Manifest changed",
            Assert.Throws<InvalidDataException>(() => Workspace.Restore(locked: true)).Message,
            StringComparison.Ordinal
        );
        Assert.Throws<InvalidDataException>(() => Workspace.Restore());
        Workspace.Restore(update: true);
    }

    [Fact]
    public void Local_changes_and_cache_tampering_are_rejected()
    {
        PortableGraph();
        Workspace.Restore();
        var library = Workspace.GetLibraries()["words"];
        File.AppendAllText(library, "\n% tampered\n");
        Assert.Contains(
            "Corrupt",
            Assert.Throws<InvalidDataException>(() => Workspace.GetLibraries()).Message,
            StringComparison.Ordinal
        );
        File.WriteAllText(library, ":- module(words, [word/1]).\nword(hello).\n");
        File.AppendAllText(Path.Combine(_directory, "words", "words.pl"), "\n% changed\n");
        Assert.Contains(
            "Local package",
            Assert.Throws<InvalidDataException>(() => Workspace.Restore(locked: true)).Message,
            StringComparison.Ordinal
        );
        Workspace.Restore(update: true);
    }

    [Fact]
    public void Missing_cache_is_rebuilt_from_lock()
    {
        PortableGraph();
        Workspace.Restore();
        Directory.Delete(Cache, true);
        Assert.Throws<InvalidDataException>(() => Workspace.GetLibraries());
        Workspace.Restore(locked: true);
        Assert.Equal(2, Workspace.GetLibraries().Count);
    }

    [Fact]
    public void Conflicting_versions_report_dependency_chain()
    {
        PortableGraph();
        var manifest = PackageWorkspace.ReadManifest(Root);
        manifest.Dependencies["demo/words"] = new PackageSource { Version = "2.0.0", Path = "../words" };
        Write(Root, manifest);
        var error = Assert.Throws<InvalidDataException>(() => Workspace.Restore());
        Assert.Contains("demo/words", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Root, "propack.lock.json")));
    }

    [Fact]
    public void Duplicate_module_names_are_rejected()
    {
        PortableGraph();
        var second = Package("other", "other/words", "1.0.0", "words", "word(other).");
        var root = PackageWorkspace.ReadManifest(Root);
        root.Dependencies["other/words"] = new PackageSource { Path = second };
        Write(Root, root);
        Assert.Contains(
            "Module conflict",
            Assert.Throws<InvalidDataException>(() => Workspace.Restore()).Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Cycles_are_rejected()
    {
        PortableGraph();
        var words = Path.Combine(_directory, "words");
        var manifest = PackageWorkspace.ReadManifest(words);
        manifest.Dependencies["demo/greeting"] = new PackageSource { Path = "../greeting" };
        Write(words, manifest);
        Assert.Contains(
            "cycle",
            Assert.Throws<InvalidDataException>(() => Workspace.Restore()).Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Catalog_solver_backtracks_and_preserves_locked_versions()
    {
        var v1 = Package("one", "demo/lib", "1.0.0", "lib", "value(one).");
        var v2 = Package("two", "demo/lib", "2.0.0", "lib", "value(two).");
        var consumer = Package("consumer", "demo/consumer", "1.0.0", "consumer", "ok.");
        var cm = PackageWorkspace.ReadManifest(consumer);
        cm.Dependencies["demo/lib"] = new PackageSource { Version = "^1.0.0" };
        Write(consumer, cm);
        Write(
            Root,
            new PackageManifest
            {
                Name = "demo/app",
                Catalog = "catalog.json",
                Dependencies = new()
                {
                    ["demo/lib"] = new() { Version = "*" },
                    ["demo/consumer"] = new() { Path = consumer },
                },
            }
        );
        Dictionary<string, List<PackageSource>> catalog = new()
        {
            ["demo/lib"] = [new() { Version = "1.0.0", Path = v1 }, new() { Version = "2.0.0", Path = v2 }],
        };
        File.WriteAllText(
            Path.Combine(Root, "catalog.json"),
            JsonSerializer.Serialize(catalog, PackageJsonContext.Default.DictionaryStringListPackageSource)
        );
        Workspace.Restore();
        Assert.Contains("demo/lib 1.0.0", Workspace.Describe(), StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(Root, "catalog.json"), "{}");
        Workspace.Restore(locked: true);
    }

    [Fact]
    public void Zip_sources_are_hashed_and_restore_without_original_archive_when_cached()
    {
        var package = Package("zip-source", "demo/lib", "1.0.0", "lib", "ok.");
        var zip = Path.Combine(_directory, "package.zip");
        ZipFile.CreateFromDirectory(package, zip);
        Write(
            Root,
            new PackageManifest
            {
                Name = "demo/app",
                Dependencies = new() { ["demo/lib"] = new() { Archive = zip } },
            }
        );
        Workspace.Restore();
        File.Delete(zip);
        Workspace.Restore(locked: true);
        Assert.Single(Workspace.GetLibraries());
    }

    [Fact]
    public void Archive_traversal_and_bad_checksums_fail_without_lock()
    {
        var zip = Path.Combine(_directory, "evil.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("../escaped.pl").Open()))
            writer.Write("bad.");
        Write(
            Root,
            new PackageManifest
            {
                Name = "demo/app",
                Dependencies = new() { ["demo/lib"] = new() { Archive = zip } },
            }
        );
        Assert.Throws<InvalidDataException>(() => Workspace.Restore());
        Assert.False(File.Exists(Path.Combine(Cache, "escaped.pl")));
        var manifest = PackageWorkspace.ReadManifest(Root);
        manifest.Dependencies["demo/lib"] = new PackageSource { Archive = zip, Sha256 = new string('0', 64) };
        Write(Root, manifest);
        Assert.Contains(
            "SHA-256",
            Assert.Throws<InvalidDataException>(() => Workspace.Restore()).Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Git_revision_is_pinned_and_new_commits_do_not_change_locked_restore()
    {
        var source = Package("git-source", "demo/lib", "1.0.0", "lib", "ok.");
        Git("init", "--quiet", source);
        Commit(source);
        Write(
            Root,
            new PackageManifest
            {
                Name = "demo/app",
                Dependencies = new()
                {
                    ["demo/lib"] = new() { Git = source, Revision = "HEAD" },
                },
            }
        );
        Workspace.Restore();
        var original = File.ReadAllText(Path.Combine(Root, "propack.lock.json"));
        File.AppendAllText(Path.Combine(source, "lib.pl"), "\nnew_fact.\n");
        Commit(source);
        Directory.Delete(Cache, true);
        Workspace.Restore(locked: true);
        Assert.Equal(original, File.ReadAllText(Path.Combine(Root, "propack.lock.json")));
        Workspace.Restore(update: true);
        Assert.NotEqual(original, File.ReadAllText(Path.Combine(Root, "propack.lock.json")));
    }

    [Fact]
    public void Lock_is_portable_to_a_different_checkout_and_cache()
    {
        PortableGraph();
        Workspace.Restore();
        var clone = Path.Combine(_directory, "clone");
        foreach (var name in new[] { "app", "words", "greeting" })
        {
            Directory.CreateDirectory(Path.Combine(clone, name));
            foreach (var file in Directory.GetFiles(Path.Combine(_directory, name)))
                File.Copy(file, Path.Combine(clone, name, Path.GetFileName(file)));
        }
        var workspace = new PackageWorkspace(Path.Combine(clone, "app"), Path.Combine(_directory, "cache2"));
        workspace.Restore(locked: true);
        Assert.Equal(2, workspace.GetLibraries().Count);
    }

    [Theory]
    [InlineData("1.9.0", "^1.2.3", true)]
    [InlineData("2.0.0", "^1.2.3", false)]
    [InlineData("0.2.9", "^0.2.3", true)]
    [InlineData("0.3.0", "^0.2.3", false)]
    [InlineData("0.0.4", "^0.0.3", false)]
    public void Caret_ranges_follow_stable_semver(string actual, string range, bool expected) =>
        Assert.Equal(expected, PackageVersions.Matches(actual, range));

    private void PortableGraph()
    {
        Package("words", "demo/words", "1.0.0", "words", "word(hello).", "word/1");
        var greeting = Package(
            "greeting",
            "demo/greeting",
            "1.0.0",
            "greeting",
            ":- use_module(library(words)).\ngreet(X) :- word(X).",
            "greet/1"
        );
        var manifest = PackageWorkspace.ReadManifest(greeting);
        manifest.Dependencies["demo/words"] = new PackageSource { Version = "^1.0.0", Path = "../words" };
        Write(greeting, manifest);
        Write(
            Root,
            new PackageManifest
            {
                Name = "demo/app",
                Dependencies = new()
                {
                    ["demo/greeting"] = new() { Path = "../greeting", Version = "^1.0.0" },
                },
            }
        );
        File.WriteAllText(
            Path.Combine(Root, "main.pl"),
            ":- use_module(library(greeting)).\n:- initialization(main).\nmain :- greet(X), writeln(X).\n"
        );
    }

    private string Package(string folder, string name, string version, string module, string code, string exports = "")
    {
        var directory = Path.Combine(_directory, folder);
        Directory.CreateDirectory(directory);
        Write(
            directory,
            new PackageManifest
            {
                Name = name,
                Version = version,
                Modules = new() { [module] = module + ".pl" },
                Engines = new() { ["dotprolog"] = "*", ["swi"] = "*" },
            }
        );
        File.WriteAllText(Path.Combine(directory, module + ".pl"), $":- module({module}, [{exports}]).\n{code}\n");
        return directory;
    }

    private static void Write(string directory, PackageManifest manifest) =>
        File.WriteAllText(
            Path.Combine(directory, "propack.json"),
            JsonSerializer.Serialize(manifest, PackageJsonContext.Default.PackageManifest)
        );

    private static void Git(params string[] args) => PackageFiles.ProcessOutput("git", args);

    private static void Commit(string source)
    {
        Git("-C", source, "add", "propack.json", "lib.pl");
        Git(
            "-C",
            source,
            "-c",
            "user.name=Package Test",
            "-c",
            "user.email=package@example.invalid",
            "-c",
            "core.hooksPath=/dev/null",
            "commit",
            "--quiet",
            "-m",
            "Test package"
        );
    }
}
