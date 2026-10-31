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

    [Theory]
    [InlineData(".", false)]
    [InlineData(".", true)]
    [InlineData("cache", false)]
    [InlineData("cache", true)]
    [InlineData("cache/snapshots", false)]
    [InlineData("cache/..", false)]
    public void Cache_must_not_equal_or_descend_from_project(string relativeCache, bool trailingProjectSeparator)
    {
        var project = trailingProjectSeparator ? Root + Path.DirectorySeparatorChar : Root;
        var error = Assert.Throws<ArgumentException>(() => new PackageWorkspace(project, Path.Combine(Root, relativeCache)));
        Assert.Equal("cacheDirectory", error.ParamName);
        Assert.Contains("outside the project", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("cache")]
    public void Cache_path_comparison_follows_platform_case_rules(string relativeCache)
    {
        var cache = Path.Combine(_directory, "APP", relativeCache);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            Assert.Throws<ArgumentException>(() => new PackageWorkspace(Root, cache));
        else
            _ = new PackageWorkspace(Root, cache);
    }

    [Fact]
    public void Cache_can_be_a_sibling_with_the_project_name_prefix()
    {
        _ = new PackageWorkspace(Root, Root + "-cache");
        _ = new PackageWorkspace(Root, _directory);
    }

    [Fact]
    public void File_system_root_project_has_no_cache_directory_outside_it_on_the_same_root()
    {
        Assert.Throws<ArgumentException>(() => new PackageWorkspace(Path.GetPathRoot(Root)!, Cache));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("cache")]
    public void Restore_rejects_cache_inside_project_before_creating_files(string relativeCache)
    {
        File.WriteAllText(Path.Combine(Root, "propack.json"), "{\"name\":\"demo/app\"}");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = PackageCommands.Execute(
            ["restore", "--project", Root, "--cache", Path.Combine(Root, relativeCache)],
            output,
            error
        );
        Assert.Equal(64, result);
        Assert.Contains("outside the project", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(Root));
        Assert.False(File.Exists(Path.Combine(Root, "propack.lock.json")));
    }

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

    [Theory]
    [InlineData("path")]
    [InlineData("git")]
    [InlineData("archive")]
    public void Catalog_transport_failure_tries_an_older_candidate(string transport)
    {
        var good = Package("good", "demo/lib", "1.0.0", "lib", "ok.");
        var missing = Path.Combine(_directory, "missing");
        var failed = transport switch
        {
            "path" => new PackageSource { Version = "2.0.0", Path = missing },
            "git" => new PackageSource
            {
                Version = "2.0.0",
                Git = missing,
                Revision = "HEAD",
            },
            _ => new PackageSource { Version = "2.0.0", Archive = missing },
        };
        UseCatalog(failed, new PackageSource { Version = "1.0.0", Path = good });
        Workspace.Restore();
        Assert.Contains("demo/lib 1.0.0", Workspace.Describe(), StringComparison.Ordinal);
        Assert.Single(Workspace.GetLibraries());
    }

    [Fact]
    public void All_catalog_transport_failures_report_candidates_and_preserve_lock()
    {
        var good = Package("good", "demo/lib", "1.0.0", "lib", "ok.");
        UseCatalog(new PackageSource { Version = "1.0.0", Path = good });
        Workspace.Restore();
        var original = File.ReadAllText(Path.Combine(Root, "propack.lock.json"));
        UseCatalog(
            new PackageSource { Version = "2.0.0", Path = Path.Combine(_directory, "missing-two") },
            new PackageSource { Version = "3.0.0", Archive = Path.Combine(_directory, "missing-three.zip") }
        );
        var error = Assert.Throws<InvalidDataException>(() => Workspace.Restore(update: true));
        Assert.Contains("demo/lib 2.0.0", error.Message, StringComparison.Ordinal);
        Assert.Contains("demo/lib 3.0.0", error.Message, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(Path.Combine(Root, "propack.lock.json")));
    }

    [Fact]
    public void Explicit_source_failure_does_not_fall_back_to_catalog()
    {
        var good = Package("good", "demo/lib", "1.0.0", "lib", "ok.");
        UseCatalog(new PackageSource { Version = "1.0.0", Path = good });
        var manifest = PackageWorkspace.ReadManifest(Root);
        manifest.Dependencies["demo/lib"] = new PackageSource { Path = Path.Combine(_directory, "missing") };
        Write(Root, manifest);
        Assert.ThrowsAny<IOException>(() => Workspace.Restore());
        Assert.False(File.Exists(Path.Combine(Root, "propack.lock.json")));
    }

    [Fact]
    public void Catalog_checksum_failure_is_fatal_instead_of_selecting_older_bytes()
    {
        var good = Package("good", "demo/lib", "1.0.0", "lib", "ok.");
        var newer = Package("newer", "demo/lib", "2.0.0", "lib", "newer.");
        var archive = Source(newer, "zip") with { Version = "2.0.0", Sha256 = new string('0', 64) };
        UseCatalog(archive, new PackageSource { Version = "1.0.0", Path = good });
        Assert.Contains(
            "SHA-256 mismatch",
            Assert.Throws<InvalidDataException>(() => Workspace.Restore()).Message,
            StringComparison.Ordinal
        );
        Assert.False(File.Exists(Path.Combine(Root, "propack.lock.json")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad")]
    public void HTTPS_archive_requires_a_valid_digest_before_fetching(string? digest)
    {
        Write(
            Root,
            new PackageManifest
            {
                Name = "demo/app",
                Dependencies = new()
                {
                    ["demo/lib"] = new PackageSource { Archive = "https://example.invalid/package.zip", Sha256 = digest },
                },
            }
        );
        Assert.Contains(
            "SHA-256",
            Assert.Throws<InvalidDataException>(() => Workspace.Restore()).Message,
            StringComparison.Ordinal
        );
        Assert.False(Directory.Exists(Cache));
        Assert.False(File.Exists(Path.Combine(Root, "propack.lock.json")));
    }

    [Theory]
    [InlineData("/* first */\n% line\n :- module ( lib , []).", true)]
    [InlineData(":- module(lib\n, []).", true)]
    [InlineData("/* unterminated", false)]
    [InlineData("% only comment", false)]
    [InlineData(":- module(liberal, []).", false)]
    [InlineData(":- module('lib', []).", false)]
    [InlineData(":- initialization(main).\n:- module(lib, []).", false)]
    public void Module_header_scanner_preserves_comment_and_identifier_rules(string text, bool expected) =>
        Assert.Equal(expected, PackageModuleHeader.Matches(text, "lib"));

    [Fact]
    public void Module_header_does_not_treat_layout_as_part_of_an_identifier() =>
        Assert.False(PackageModuleHeader.Matches(":- module(lib\n, []).", "lib\n"));

    [Theory]
    [InlineData(65000, true)]
    [InlineData(66000, false)]
    public void Module_header_scan_is_bounded_to_the_first_64_KiB(int commentLength, bool accepted)
    {
        Write(
            Root,
            new PackageManifest
            {
                Name = "demo/app",
                Modules = new() { ["lib"] = "lib.pl" },
            }
        );
        File.WriteAllText(
            Path.Combine(Root, "lib.pl"),
            "/*" + new string(' ', commentLength) + "*/\n:- module(lib, []).\n%" + new string('x', 1024 * 1024)
        );
        if (accepted)
        {
            Workspace.Restore();
            Assert.Single(Workspace.GetLibraries());
        }
        else
            Assert.Contains(
                "64 KiB",
                Assert.Throws<InvalidDataException>(() => Workspace.Restore()).Message,
                StringComparison.Ordinal
            );
        Assert.Equal(64 * 1024, PackageFiles.ReadHeader(Root, "lib.pl").Length);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("git")]
    [InlineData("zip")]
    public void Every_transport_publishes_only_included_files(string transport)
    {
        var package = Package("source", "demo/lib", "1.0.0", "lib", "ok.");
        foreach (
            var relative in new[]
            {
                "bin/ignored.inc",
                "nested/obj/ignored.inc",
                "nested/.propack/ignored.inc",
                "nested/propack.lock.json",
            }
        )
        {
            var file = Path.Combine(package, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "excluded");
        }
        Directory.CreateDirectory(Path.Combine(package, "empty"));
        var acquired = PackageFiles.Acquire(Source(package, transport), Cache);
        Assert.Equal(2, Directory.GetFiles(acquired.Directory).Length);
        Assert.True(File.Exists(Path.Combine(acquired.Directory, "lib.pl")));
        Assert.True(File.Exists(Path.Combine(acquired.Directory, "propack.json")));
        Assert.Empty(Directory.GetDirectories(acquired.Directory));
        Assert.Equal(PackageFiles.HashTree(package), PackageFiles.HashTree(acquired.Directory, requireSnapshot: true));
        Assert.Single(Directory.GetDirectories(Cache));
    }

    [Theory]
    [InlineData("path", "bin")]
    [InlineData("git", "bin")]
    [InlineData("zip", "bin")]
    [InlineData("zip", "nested/obj")]
    [InlineData("zip", "nested/.propack")]
    [InlineData("zip", "nested/.git")]
    [InlineData("zip", "nested/propack.lock.json")]
    public void Modules_in_excluded_paths_are_rejected(string transport, string excluded)
    {
        var package = Package("source", "demo/lib", "1.0.0", "lib", "ok.");
        Directory.CreateDirectory(Path.Combine(package, excluded));
        File.Move(Path.Combine(package, "lib.pl"), Path.Combine(package, excluded, "lib.pl"));
        var manifest = PackageWorkspace.ReadManifest(package);
        manifest.Modules["lib"] = excluded + "/lib.pl";
        Write(package, manifest);
        Write(
            Root,
            new PackageManifest
            {
                Name = "demo/app",
                Dependencies = new() { ["demo/lib"] = Source(package, transport) },
            }
        );
        Assert.Contains(
            "excluded",
            Assert.Throws<InvalidDataException>(() => Workspace.Restore()).Message,
            StringComparison.Ordinal
        );
        Assert.False(File.Exists(Path.Combine(Root, "propack.lock.json")));
    }

    [Fact]
    public void Root_modules_cannot_use_excluded_paths()
    {
        Directory.CreateDirectory(Path.Combine(Root, "bin"));
        File.WriteAllText(Path.Combine(Root, "bin/lib.pl"), ":- module(lib, []).");
        Write(
            Root,
            new PackageManifest
            {
                Name = "demo/app",
                Modules = new() { ["lib"] = "bin/lib.pl" },
            }
        );
        Assert.Contains(
            "excluded",
            Assert.Throws<InvalidDataException>(() => Workspace.Restore()).Message,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("bin/hidden.inc", false)]
    [InlineData("nested/obj/hidden.inc", false)]
    [InlineData("nested/.git/hidden.inc", false)]
    [InlineData("nested/.propack/hidden.inc", false)]
    [InlineData("nested/propack.lock.json", false)]
    [InlineData("bin", true)]
    [InlineData("unexpected-empty", true)]
    public void Cache_hits_reject_unhashed_paths(string relative, bool emptyDirectory)
    {
        PortableGraph();
        Workspace.Restore();
        var snapshot = Path.GetDirectoryName(Workspace.GetLibraries()["words"])!;
        var added = Path.Combine(snapshot, relative);
        Directory.CreateDirectory(emptyDirectory ? added : Path.GetDirectoryName(added)!);
        if (!emptyDirectory)
            File.WriteAllText(added, "tampered");
        Assert.Contains(
            "Corrupt",
            Assert.Throws<InvalidDataException>(() => Workspace.GetLibraries()).Message,
            StringComparison.Ordinal
        );
        Assert.Throws<InvalidDataException>(() => Workspace.Restore(locked: true));
        Assert.Throws<InvalidDataException>(() => Workspace.Restore(update: true));
    }

    [Fact]
    public void Archives_with_equal_included_files_share_only_those_files()
    {
        var package = Package("source", "demo/lib", "1.0.0", "lib", "ok.");
        Directory.CreateDirectory(Path.Combine(package, "bin"));
        File.WriteAllText(Path.Combine(package, "bin/hidden.inc"), "first");
        var first = PackageFiles.Acquire(Source(package, "zip"), Cache);
        File.WriteAllText(Path.Combine(package, "bin/hidden.inc"), "second");
        var secondZip = Path.Combine(_directory, "second.zip");
        ZipFile.CreateFromDirectory(package, secondZip);
        var second = PackageFiles.Acquire(new PackageSource { Archive = secondZip }, Cache);
        Assert.NotEqual(first.Source.Sha256, second.Source.Sha256);
        Assert.Equal(first.Directory, second.Directory);
        Assert.False(Directory.Exists(Path.Combine(second.Directory, "bin")));
        Assert.Equal(2, Directory.GetFiles(second.Directory, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Existing_archive_cache_with_excluded_content_is_rejected()
    {
        var package = Package("source", "demo/lib", "1.0.0", "lib", "ok.");
        Directory.CreateDirectory(Path.Combine(package, "bin"));
        File.WriteAllText(Path.Combine(package, "bin/hidden.inc"), "legacy snapshot");
        var source = Source(package, "zip");
        var legacy = Path.Combine(Cache, PackageFiles.HashTree(package));
        ZipFile.ExtractToDirectory(source.Archive!, legacy);
        Assert.Contains(
            "Corrupt",
            Assert.Throws<InvalidDataException>(() => PackageFiles.Acquire(source, Cache)).Message,
            StringComparison.Ordinal
        );
        Assert.Single(Directory.GetDirectories(Cache));
    }

    [Theory]
    [InlineData("git")]
    [InlineData("revision")]
    [InlineData("archive")]
    public void Source_control_characters_are_rejected_before_acquisition(string field)
    {
        var source = field switch
        {
            "git" => new PackageSource { Git = "https://example.invalid/repo\n", Revision = "HEAD" },
            "revision" => new PackageSource { Git = "https://example.invalid/repo", Revision = "HEAD\r\nother" },
            _ => new PackageSource { Archive = "https://example.invalid/package.zip\n" },
        };
        Write(
            Root,
            new PackageManifest
            {
                Name = "demo/app",
                Dependencies = new() { ["demo/lib"] = source },
            }
        );
        Assert.Contains(
            "control characters",
            Assert.Throws<InvalidDataException>(() => Workspace.Restore()).Message,
            StringComparison.Ordinal
        );
        Assert.False(Directory.Exists(Cache));
    }

    [Fact]
    public void Git_submodules_are_rejected_without_fetching_them()
    {
        var package = Package("source", "demo/lib", "1.0.0", "lib", "ok.");
        File.WriteAllText(
            Path.Combine(package, ".gitmodules"),
            "[submodule \"bad\"]\npath = bad\nurl = https://example.invalid/bad\n"
        );
        var source = Source(package, "git");
        Assert.Contains(
            "submodules",
            Assert.Throws<InvalidDataException>(() => PackageFiles.Acquire(source, Cache)).Message,
            StringComparison.Ordinal
        );
        Assert.Empty(Directory.GetFileSystemEntries(Cache));
    }

    [Fact]
    public void Git_symlinks_are_materialized_as_text_without_reading_the_target()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links can require additional Windows privileges.");
        var package = Package("source", "demo/lib", "1.0.0", "lib", "ok.");
        var secret = Path.Combine(_directory, "secret.txt");
        File.WriteAllText(secret, "outside package");
        File.CreateSymbolicLink(Path.Combine(package, "link.inc"), secret);
        var acquired = PackageFiles.Acquire(Source(package, "git"), Cache);
        var link = Path.Combine(acquired.Directory, "link.inc");
        Assert.Equal((FileAttributes)0, File.GetAttributes(link) & FileAttributes.ReparsePoint);
        Assert.Equal(secret, File.ReadAllText(link));
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

    [Fact]
    public void Policy_blocks_a_known_vulnerability_before_writing_the_lock()
    {
        var package = Package("source", "demo/lib", "1.0.0", "lib", "ok.");
        Write(
            Root,
            new PackageManifest
            {
                Name = "demo/app",
                Dependencies = new() { ["demo/lib"] = new() { Path = package } },
            }
        );
        WritePolicy(
            """
            {
              "advisories": [
                { "id": "PROPACK-1", "package": "demo/lib", "version": "1.0.0", "summary": "Known bad release." }
              ]
            }
            """
        );
        Assert.Contains(
            "PROPACK-1",
            Assert.Throws<InvalidDataException>(() => Workspace.Restore()).Message,
            StringComparison.Ordinal
        );
        Assert.False(File.Exists(Path.Combine(Root, "propack.lock.json")));
        WritePolicy(
            """
            {
              "advisories": [
                {
                  "id": "PROPACK-1",
                  "package": "demo/lib",
                  "hash": "0000000000000000000000000000000000000000000000000000000000000000",
                  "summary": "Different tree."
                }
              ]
            }
            """
        );
        Workspace.Restore();
        var locked = File.ReadAllText(Path.Combine(Root, "propack.lock.json"));
        WritePolicy(
            """
            {
              "advisoryFiles": ["feed.json"],
              "ignore": ["PROPACK-1"],
              "advisories": [
                { "id": "PROPACK-2", "package": "demo/lib", "version": "1.0.0", "summary": "Still bad." }
              ]
            }
            """
        );
        File.WriteAllText(
            Path.Combine(Root, "feed.json"),
            """
            { "advisories": [ { "id": "PROPACK-1", "package": "demo/lib", "version": "1.0.0", "summary": "Ignored." } ] }
            """
        );
        Assert.Contains(
            "PROPACK-2",
            Assert.Throws<InvalidDataException>(() => Workspace.Restore()).Message,
            StringComparison.Ordinal
        );
        Assert.Equal(locked, File.ReadAllText(Path.Combine(Root, "propack.lock.json")));
        File.Delete(Path.Combine(Root, "propack.policy.json"));
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, PackageCommands.Execute(["check", "--project", Root, "--cache", Cache], output, error));
        Assert.Contains("not applied", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Policy_filters_declared_licenses_on_restore_and_compilation()
    {
        var package = Package("source", "demo/lib", "1.0.0", "lib", "ok.");
        File.WriteAllText(Path.Combine(package, "UPSTREAM.json"), """{ "license": "(MIT OR GPL-3.0) AND BSD-2-Clause" }""");
        File.WriteAllText(Path.Combine(Root, "UPSTREAM.json"), """{ "license": "MIT WITH LLVM-exception" }""");
        Write(
            Root,
            new PackageManifest
            {
                Name = "demo/app",
                Dependencies = new() { ["demo/lib"] = new() { Path = package } },
            }
        );
        WritePolicy("""{ "licenses": ["MIT", "BSD-2-Clause", "MIT WITH LLVM-exception"] }""");
        Workspace.Restore();
        Assert.Contains("verified", Workspace.Check(), StringComparison.Ordinal);
        WritePolicy("""{ "licenses": ["MIT"] }""");
        Assert.Contains(
            "not allowed",
            Assert.Throws<InvalidDataException>(() => Workspace.GetLibraries()).Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "not allowed",
            Assert.Throws<InvalidDataException>(() => Workspace.Restore()).Message,
            StringComparison.Ordinal
        );
        WritePolicy("""{ "licenses": ["MIT"], "ignore": ["ABSENT"] }""");
        Assert.Contains(
            "not listed",
            Assert.Throws<InvalidDataException>(() => Workspace.Check()).Message,
            StringComparison.Ordinal
        );
    }

    private void WritePolicy(string json) => File.WriteAllText(Path.Combine(Root, "propack.policy.json"), json);

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

    private PackageSource Source(string directory, string transport)
    {
        if (transport == "path")
            return new PackageSource { Path = directory };
        if (transport == "zip")
        {
            var zip = Path.Combine(_directory, "package.zip");
            ZipFile.CreateFromDirectory(directory, zip);
            return new PackageSource { Archive = zip };
        }
        Git("init", "--quiet", directory);
        Commit(directory);
        return new PackageSource { Git = directory, Revision = "HEAD" };
    }

    private void UseCatalog(params PackageSource[] sources)
    {
        Write(
            Root,
            new PackageManifest
            {
                Name = "demo/app",
                Catalog = "catalog.json",
                Dependencies = new() { ["demo/lib"] = new() },
            }
        );
        Dictionary<string, List<PackageSource>> catalog = new() { ["demo/lib"] = [.. sources] };
        File.WriteAllText(
            Path.Combine(Root, "catalog.json"),
            JsonSerializer.Serialize(catalog, PackageJsonContext.Default.DictionaryStringListPackageSource)
        );
    }

    private static void Git(params string[] args) => PackageFiles.ProcessOutput("git", args);

    private static void Commit(string source)
    {
        Git("-C", source, "-c", "core.autocrlf=false", "add", "--force", ".");
        var hooks = Path.Combine(source, ".git", "propack-test-hooks");
        Directory.CreateDirectory(hooks);
        Git(
            "-C",
            source,
            "-c",
            "user.name=Package Test",
            "-c",
            "user.email=package@example.invalid",
            "-c",
            "core.hooksPath=" + hooks,
            "commit",
            "--quiet",
            "-m",
            "Test package"
        );
    }
}
