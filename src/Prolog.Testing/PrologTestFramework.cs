using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Extensions.TestFramework;
using Microsoft.Testing.Platform.Requests;
using Prolog.Runtime;

namespace Prolog.Testing;

/// <summary>
/// Runs a <c>.dplproj</c> test project's predicates under Microsoft.Testing.Platform, so that
/// <c>dotnet test</c> reports Prolog tests like any other .NET tests.
/// </summary>
/// <remarks>
/// The platform is the modern route and needs no VSTest adapter: the test project is an executable
/// that hosts this framework, which the SDK's generated entry point starts.
/// </remarks>
public sealed class PrologTestFramework : ITestFramework, IDataProducer
{
    private readonly PrologTestRunner _runner;

    /// <summary>Creates a framework over the test project's Prolog sources.</summary>
    /// <param name="sources">Each source file's name and contents.</param>
    public PrologTestFramework(IReadOnlyList<(string Name, string Text)> sources) => _runner = new PrologTestRunner(sources);

    /// <inheritdoc />
    public string Uid => "DotProlog.TestFramework";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public string DisplayName => "DotProlog";

    /// <inheritdoc />
    public string Description => "Runs zero-arity predicates named test_* as tests.";

    /// <inheritdoc />
    public Task<bool> IsEnabledAsync() => Task.FromResult(true);

    /// <summary>The message types this framework publishes; the platform routes on them.</summary>
    public Type[] DataTypesProduced => [typeof(TestNodeUpdateMessage)];

    /// <inheritdoc />
    public Task<CreateTestSessionResult> CreateTestSessionAsync(CreateTestSessionContext context) =>
        Task.FromResult(new CreateTestSessionResult { IsSuccess = true });

    /// <inheritdoc />
    public Task<CloseTestSessionResult> CloseTestSessionAsync(CloseTestSessionContext context) =>
        Task.FromResult(new CloseTestSessionResult { IsSuccess = true });

    /// <inheritdoc />
    public async Task ExecuteRequestAsync(ExecuteRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        switch (context.Request)
        {
            case DiscoverTestExecutionRequest discover:
                await PublishAsync(context, discover.Session.SessionUid, run: false).ConfigureAwait(false);
                break;

            case RunTestExecutionRequest run:
                await PublishAsync(context, run.Session.SessionUid, run: true).ConfigureAwait(false);
                break;

            default:
                break;
        }

        context.Complete();
    }

    private async Task PublishAsync(
        ExecuteRequestContext context,
        Microsoft.Testing.Platform.TestHost.SessionUid session,
        bool run
    )
    {
        foreach (PrologTest test in _runner.Discover())
        {
            var node = new TestNode { Uid = new TestNodeUid(test.Uid), DisplayName = test.Name };

            if (!run)
            {
                node.Properties.Add(DiscoveredTestNodeStateProperty.CachedInstance);
            }
            else
            {
                PrologTestResult result = _runner.Run(test);
                node.Properties.Add(
                    result.Succeeded
                        ? PassedTestNodeStateProperty.CachedInstance
                        : new FailedTestNodeStateProperty(new PrologException(Describe(result)), result.Message)
                );
            }

            await context.MessageBus.PublishAsync(this, new TestNodeUpdateMessage(session, node)).ConfigureAwait(false);
        }
    }

    /// <summary>Puts whatever the test wrote next to the reason it failed; output is often the clue.</summary>
    private static string Describe(PrologTestResult result) =>
        string.IsNullOrEmpty(result.Output) ? result.Message! : $"{result.Message}\n{result.Output}";
}
