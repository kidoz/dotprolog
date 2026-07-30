using Microsoft.Testing.Platform.Builder;
using Microsoft.Testing.Platform.Capabilities.TestFramework;

namespace DotProlog.Testing;

/// <summary>
/// Starts the test platform for a <c>.dplproj</c> test project. The SDK's generated entry point calls
/// this with the project's Prolog sources.
/// </summary>
public static class PrologTestHost
{
    /// <summary>Runs the test application and returns its exit code.</summary>
    /// <param name="arguments">Command line arguments, passed through to the platform.</param>
    /// <param name="sources">Each Prolog source file's name and contents.</param>
    public static async Task<int> RunAsync(string[] arguments, IReadOnlyList<(string Name, string Text)> sources)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(sources);

        ITestApplicationBuilder builder = await TestApplication.CreateBuilderAsync(arguments).ConfigureAwait(false);
        builder.RegisterTestFramework(_ => new TestFrameworkCapabilities(), (_, _) => new PrologTestFramework(sources));

        using ITestApplication application = await builder.BuildAsync().ConfigureAwait(false);
        return await application.RunAsync().ConfigureAwait(false);
    }

    /// <summary>Runs a test application using build-time-generated predicates.</summary>
    public static async Task<int> RunAsync(string[] arguments, Func<DotProlog.Compiler.PrologEngine> engineFactory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(engineFactory);

        ITestApplicationBuilder builder = await TestApplication.CreateBuilderAsync(arguments).ConfigureAwait(false);
        builder.RegisterTestFramework(_ => new TestFrameworkCapabilities(), (_, _) => new PrologTestFramework(engineFactory));

        using ITestApplication application = await builder.BuildAsync().ConfigureAwait(false);
        return await application.RunAsync().ConfigureAwait(false);
    }
}
