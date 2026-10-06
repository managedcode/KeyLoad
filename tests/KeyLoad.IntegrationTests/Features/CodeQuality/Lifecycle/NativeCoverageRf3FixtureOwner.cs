using Aspire.Hosting;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

/// <summary>Retains the original selected collector cohort, native policy and case ledger until actual settlement.</summary>
internal sealed class NativeCoverageRf3FixtureOwner
{
    private readonly NativeCoverageRf3FixtureContext context;
    private readonly ClusterFixtureSourceImage sourceImage;
    private readonly IOptions<NativeCoverageExecutionOptions> options;
    private readonly HashSet<NativeCoverageRf3CaseIdentity> executedCases = [];
    private readonly Lock caseGate = new();
    private IReadOnlyDictionary<string, ContainerRuntimeInspection>? startedNodes;

    private NativeCoverageRf3FixtureOwner(NativeCoverageRf3FixtureContext context,
        ClusterFixtureSourceImage sourceImage, IOptions<NativeCoverageExecutionOptions> options)
    {
        this.context = context;
        this.sourceImage = sourceImage;
        this.options = options;
    }

    internal string Root => Path.Combine(context.FixtureRoot, NativeCoverageRf3FixtureProtocol.StorageDirectoryName);

    internal static async Task<NativeCoverageRf3FixtureOwner?> CreateAsync(string fixtureId, CancellationToken token)
    {
        var execution = NativeCoverageRf3FixtureOptions.Read();
        if (execution is null)
        {
            return null;
        }
        var context = await NativeCoverageRf3FixtureContextReader.ReadAsync(fixtureId, execution, token)
            .ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }
        var sourceImage = await ClusterFixtureImageIdentity.ReadVerifiedImageAsync(token).ConfigureAwait(false);
        return new(context, sourceImage, execution);
    }

    internal string[] CreateArguments()
    {
        Directory.CreateDirectory(context.FixtureRoot);
        var startupPoll = NativeCoverageRf3FixtureOptions.RequiredEnvironment(
            NativeCoverageRf3FixtureProtocol.StartupPollEnvironment);
        return NativeCoverageRf3FixtureArguments.Create(context, Root, startupPoll);
    }

    internal Task VerifyBeforeStartAsync(DistributedApplication app, CancellationToken token)
        => NativeCoverageRf3ImageIdentity.VerifyBeforeStartAsync(app, context, token);

    internal async Task VerifyStartedAsync(IReadOnlyDictionary<string, string> names, CancellationToken token)
        => startedNodes = await NativeCoverageRf3ImageIdentity.VerifyStartedAsync(names, context, token)
            .ConfigureAwait(false);

    internal void RegisterCase<TCase>(string methodName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        var className = typeof(TCase).FullName
            ?? throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidRun);
        var identity = new NativeCoverageRf3CaseIdentity(className, methodName, methodName);
        lock (caseGate)
        {
            if (!context.SelectedCases.Contains(identity) || !executedCases.Add(identity))
            {
                throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidRun);
            }
        }
    }

    internal NativeCoverageCleanupDeadline CreateCleanupDeadline() => new(options.Value.ApplicationCleanupTimeout);

    internal async Task<IReadOnlyDictionary<string, ContainerRuntimeInspection>?> VerifyStoppedAsync(
        IReadOnlyDictionary<string, string> names, NativeCoverageCleanupDeadline deadline, CancellationToken token)
    {
        if (startedNodes is null)
        {
            return null;
        }
        return await deadline.WaitAsync(() => NativeCoverageRf3ImageIdentity.VerifyStoppedAsync(
            names, startedNodes, context, token)).ConfigureAwait(false);
    }

    internal Task PublishReceiptAsync(IReadOnlyDictionary<string, ContainerRuntimeInspection> stoppedNodes,
        CancellationToken token)
    {
        NativeCoverageRf3CaseIdentity[] executed;
        lock (caseGate)
        {
            executed = executedCases.ToArray();
        }
        return NativeCoverageRf3FixtureReceiptWriter.WriteAsync(context, sourceImage, executed,
            stoppedNodes, options, token);
    }
}
