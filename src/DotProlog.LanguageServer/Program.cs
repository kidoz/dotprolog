using DotProlog.Runtime;

namespace DotProlog.LanguageServer;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        PrologLanguageMode mode = PrologLanguageMode.Modern;
        string? projectPath = null;
        string configuration = "Debug";
        HashSet<string> options = new(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 == args.Length || !options.Add(args[index]))
                return Usage();
            switch (args[index])
            {
                case "--mode":
                    if (!PrologLanguageModes.TryParse(args[index + 1], out mode))
                        return Usage();
                    break;
                case "--project":
                    projectPath = args[index + 1];
                    break;
                case "--configuration":
                    configuration = args[index + 1];
                    break;
                default:
                    return Usage();
            }
        }
        if (
            (projectPath is not null && options.Contains("--mode"))
            || (projectPath is null && options.Contains("--configuration"))
        )
            return Usage();

        try
        {
            ProjectConfiguration? project = projectPath is null
                ? null
                : await ProjectEvaluator.EvaluateAsync(projectPath, configuration).ConfigureAwait(false);
            Func<CancellationToken, Task<ProjectConfiguration>>? reload = project is null
                ? null
                : token => ProjectEvaluator.EvaluateAsync(project.ProjectPath, configuration, token);
            return await new LanguageServer(mode, project, reload)
                .RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput())
                .ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or InvalidDataException
                        or System.Text.Json.JsonException
                        or System.ComponentModel.Win32Exception
                        or ArgumentException
                        or InvalidOperationException
            )
        {
            await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return projectPath is null ? 1 : 65;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine(
            "Usage: dotprolog-lsp [--mode modern|strict-iso] | --project file.dplproj [--configuration Debug]"
        );
        return 64;
    }
}
