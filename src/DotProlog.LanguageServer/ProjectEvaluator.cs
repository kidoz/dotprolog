using System.Diagnostics;

namespace DotProlog.LanguageServer;

internal static class ProjectEvaluator
{
    internal static async Task<ProjectConfiguration> EvaluateAsync(
        string path,
        string configuration,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        if (!File.Exists(path) || !path.EndsWith(".dplproj", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("--project must name an existing .dplproj file.");
        if (
            configuration.Length == 0
            || configuration.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.'))
        )
            throw new InvalidDataException("Configuration must contain only letters, digits, dots, underscores or hyphens.");
        ProcessStartInfo start = new("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(path)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (
            string argument in new[]
            {
                "msbuild",
                path,
                "-nologo",
                "-nodeReuse:false",
                "-getProperty:MSBuildProjectFullPath,DotPrologLanguageMode,DotPrologFlags,TargetFramework,TargetFrameworks",
                "-getItem:PrologCompile,PrologContract",
                "-p:DesignTimeBuild=true",
                $"-p:Configuration={configuration}",
            }
        )
            start.ArgumentList.Add(argument);
        start.Environment["DOTNET_NOLOGO"] = "1";
        using Process process = Process.Start(start) ?? throw new IOException("Could not start MSBuild.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            string text = await output.ConfigureAwait(false);
            string error = await errors.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidDataException($"Project evaluation failed: {error}{text}");
            return ProjectConfiguration.Parse(text);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The evaluator can exit between the state check and cancellation's kill.
            }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            // Observe cancelled pipe reads before disposing the process.
            try
            {
                await Task.WhenAll(output, errors).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("Project evaluation exceeded 30 seconds.");
        }
    }
}
