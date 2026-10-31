using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DotProlog.Packages;

internal static class PackageFiles
{
    private const long MaxBytes = 128 * 1024 * 1024;

    public static string HashFile(string path)
    {
        using var input = PackageInput.OpenRead(path);
        return HashStream(input, MaxBytes).Hash;
    }

    private static (string Hash, long Bytes) HashInput(string root, string relative, long limit)
    {
        using var input = PackageInput.OpenRead(root, relative);
        return HashStream(input, limit);
    }

    private static (string Hash, long Bytes) HashStream(Stream input, long limit)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        long total = 0;
        try
        {
            int count;
            while ((count = input.Read(buffer)) != 0)
            {
                total += count;
                if (total > limit)
                    throw new InvalidDataException("Package exceeds 128 MiB of input bytes.");
                hash.AppendData(buffer, 0, count);
            }
            return (Convert.ToHexStringLower(hash.GetHashAndReset()), total);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal static string ReadText(string path)
    {
        using var input = PackageInput.OpenRead(path);
        using var bytes = new MemoryStream();
        long total = 0;
        CopyBounded(input, bytes, ref total);
        bytes.Position = 0;
        using var reader = new StreamReader(bytes);
        return reader.ReadToEnd();
    }

    internal static string ReadHeader(string root, string relative)
    {
        using var input = PackageInput.OpenRead(root, relative);
        const int headerBytes = 64 * 1024;
        var buffer = ArrayPool<byte>.Shared.Rent(headerBytes);
        try
        {
            var count = 0;
            while (count < headerBytes)
            {
                var read = input.Read(buffer, count, headerBytes - count);
                if (read == 0)
                    break;
                count += read;
            }
            using var bytes = new MemoryStream(buffer, 0, count);
            using var reader = new StreamReader(bytes);
            return reader.ReadToEnd();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static string Inside(string root, string relative)
    {
        var fullRoot = System.IO.Path.GetFullPath(root);
        var result = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot, relative));
        var prefix = System.IO.Path.EndsInDirectorySeparator(fullRoot)
            ? fullRoot
            : fullRoot + System.IO.Path.DirectorySeparatorChar;
        if (result == fullRoot || !result.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException($"Path escapes package: {relative}");
        return result;
    }

    internal static bool IsExcludedName(string name) => name is ".git" or "bin" or "obj" or ".propack" or "propack.lock.json";

    public static List<string> Files(string root, bool requireSnapshot = false)
    {
        List<string> result = [];
        long total = 0;
        Walk(root);
        result.Sort(StringComparer.Ordinal);
        return result;
        void Walk(string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Symbolic links are not package inputs: {directory}");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                var name = System.IO.Path.GetFileName(entry);
                if (IsExcludedName(name))
                {
                    if (requireSnapshot)
                        throw new InvalidDataException($"Corrupt cached package: excluded path {entry}.");
                    continue;
                }
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Symbolic links are not package inputs: {entry}");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    var count = result.Count;
                    Walk(entry);
                    if (requireSnapshot && result.Count == count)
                        throw new InvalidDataException($"Corrupt cached package: empty directory {entry}.");
                }
                else
                {
                    total += new FileInfo(entry).Length;
                    if (total > MaxBytes || result.Count >= 10000)
                        throw new InvalidDataException("Package exceeds 128 MiB or 10000 files.");
                    result.Add(System.IO.Path.GetRelativePath(root, entry).Replace('\\', '/'));
                }
            }
        }
    }

    public static string HashTree(string root, bool requireSnapshot = false)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        foreach (var file in Files(root, requireSnapshot))
        {
            var (digest, bytes) = HashInput(root, file, MaxBytes - total);
            total += bytes;
            hash.AppendData(Encoding.UTF8.GetBytes(file + "\0" + digest + "\n"));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static void CopyTree(string source, string destination)
    {
        long total = 0;
        foreach (var file in Files(source))
        {
            var target = Inside(destination, file);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            using var input = PackageInput.OpenRead(source, file);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            CopyBounded(input, output, ref total);
        }
    }

    public static (string Directory, PackageSource Source) Acquire(PackageSource source, string cache)
    {
        ValidateSourceText(source);
        var staging = System.IO.Path.Combine(cache, "stage-" + Guid.NewGuid().ToString("N"));
        var snapshot = staging + "-snapshot";
        Directory.CreateDirectory(staging);
        try
        {
            if (source.Git is not null)
            {
                if (string.IsNullOrWhiteSpace(source.Revision) || source.Revision.StartsWith('-'))
                    throw new InvalidDataException("Git sources require an explicit revision.");
                ProcessOutput(
                    "git",
                    [
                        "-c",
                        "core.hooksPath=/dev/null",
                        "-c",
                        "core.autocrlf=false",
                        "clone",
                        "--quiet",
                        "--no-checkout",
                        "--no-hardlinks",
                        "--",
                        source.Git,
                        staging,
                    ]
                );
                var commit = ProcessOutput("git", ["-C", staging, "rev-parse", "--verify", source.Revision + "^{commit}"]).Trim();
                ProcessOutput(
                    "git",
                    [
                        "-C",
                        staging,
                        "-c",
                        "core.hooksPath=/dev/null",
                        "-c",
                        "core.autocrlf=false",
                        "checkout",
                        "--quiet",
                        "--detach",
                        commit,
                    ]
                );
                if (File.Exists(System.IO.Path.Combine(staging, ".gitmodules")))
                    throw new InvalidDataException("Git submodules are not supported.");
                DeleteDirectory(System.IO.Path.Combine(staging, ".git"));
                source = source with { Revision = commit };
            }
            else if (source.Archive is not null)
            {
                var zipPath = staging + ".zip";
                try
                {
                    if (Uri.TryCreate(source.Archive, UriKind.Absolute, out var uri) && uri.Scheme == "https")
                    {
                        DownloadArchive(uri, zipPath).GetAwaiter().GetResult();
                    }
                    else
                    {
                        using var input = PackageInput.OpenRead(source.Archive);
                        using var output = File.Create(zipPath);
                        long archiveBytes = 0;
                        CopyBounded(input, output, ref archiveBytes);
                    }
                    var archiveHash = HashFile(zipPath);
                    if (
                        source.Sha256 is not null
                        && !string.Equals(source.Sha256, archiveHash, StringComparison.OrdinalIgnoreCase)
                    )
                        throw new InvalidDataException("Archive SHA-256 mismatch.");
                    source = source with { Sha256 = archiveHash };
                    using var zip = ZipFile.OpenRead(zipPath);
                    long total = 0;
                    if (zip.Entries.Count > 10000)
                        throw new InvalidDataException("Archive has too many entries.");
                    foreach (var entry in zip.Entries)
                    {
                        var target = Inside(staging, entry.FullName.Replace('\\', '/'));
                        var kind = (entry.ExternalAttributes >> 16) & 0xF000;
                        if (kind is not (0 or 0x8000 or 0x4000))
                            throw new InvalidDataException("Archive contains a non-regular entry.");
                        total += entry.Length;
                        if (total > MaxBytes)
                            throw new InvalidDataException("Archive exceeds 128 MiB.");
                        if (entry.FullName.EndsWith('/'))
                            Directory.CreateDirectory(target);
                        else
                        {
                            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                            entry.ExtractToFile(target);
                        }
                    }
                }
                finally
                {
                    if (File.Exists(zipPath))
                        File.Delete(zipPath);
                }
            }
            else if (source.Path is null)
                throw new InvalidDataException("Package source must specify path, git, or archive.");
            Directory.CreateDirectory(snapshot);
            CopyTree(source.Path ?? staging, snapshot);
            var digest = HashTree(snapshot, requireSnapshot: true);
            var final = System.IO.Path.Combine(cache, digest);
            if (!Directory.Exists(final))
            {
                try
                {
                    Directory.Move(snapshot, final);
                }
                catch (IOException) when (Directory.Exists(final)) { }
            }
            if (HashTree(final, requireSnapshot: true) != digest)
                throw new InvalidDataException($"Corrupt cached package: {digest}");
            return (final, source);
        }
        finally
        {
            if (Directory.Exists(staging))
                DeleteDirectory(staging);
            if (Directory.Exists(snapshot))
                DeleteDirectory(snapshot);
        }
    }

    internal static void ValidateSourceText(PackageSource source)
    {
        foreach (var value in new[] { source.Git, source.Revision, source.Archive })
            if (value is not null && value.Any(char.IsControl))
                throw new InvalidDataException(
                    "Git locations, revisions and archive locations cannot contain control characters."
                );
        if (source.Sha256 is not null && (source.Sha256.Length != 64 || source.Sha256.Any(c => !Uri.IsHexDigit(c))))
            throw new InvalidDataException("Archive SHA-256 must contain exactly 64 hexadecimal digits.");
        if (
            source.Archive is not null
            && Uri.TryCreate(source.Archive, UriKind.Absolute, out var uri)
            && uri.Scheme == "https"
            && source.Sha256 is null
        )
            throw new InvalidDataException("HTTPS archives require an explicit SHA-256 digest before downloading.");
    }

    internal static void DeleteDirectory(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    Directory.Delete(entry);
                else
                    DeleteDirectory(entry);
            }
            else
            {
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
                File.Delete(entry);
            }
        }
        Directory.Delete(directory);
    }

    internal static async Task DownloadArchive(Uri uri, string path, HttpClientHandler? handler = null)
    {
        using var transport = handler ?? new HttpClientHandler();
        transport.AllowAutoRedirect = false;
        transport.CheckCertificateRevocationList = true;
        using var client = new HttpClient(transport, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(2) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var response = await client
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var output = File.Create(path);
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > MaxBytes)
                throw new InvalidDataException("Archive exceeds 128 MiB.");
            await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
        }
    }

    internal static void CopyBounded(Stream input, Stream output, ref long total)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int count;
            while ((count = input.Read(buffer)) != 0)
            {
                total += count;
                if (total > MaxBytes)
                    throw new InvalidDataException("Package exceeds 128 MiB of input bytes.");
                output.Write(buffer, 0, count);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static string ProcessOutput(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException($"Could not start {executable}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000))
        {
            process.Kill(true);
            process.WaitForExit();
            throw new IOException($"{executable} timed out.");
        }
        var output = stdout.GetAwaiter().GetResult();
        var error = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new IOException($"{executable} failed: {error.Trim()}");
        return output;
    }

    public static void WriteAtomic(string path, string contents)
    {
        if (File.Exists(path) && PackageFiles.ReadText(path) == contents)
            return;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, contents);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
