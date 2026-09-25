namespace DotProlog.Compiler.Cli;

internal static class CompilationArtifacts
{
    internal static async Task WriteAsync(string destination, byte[] assembly, CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".plc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(staging, "PrologProgram.dll"), assembly, cancellationToken)
                .ConfigureAwait(false);
            PublishingProject.Write(staging);
            cancellationToken.ThrowIfCancellationRequested();
            // Moving a sibling directory publishes the complete artifact set and rejects a racing
            // compiler that created the requested destination after argument validation.
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }
}
