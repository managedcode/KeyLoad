using Aspire.Hosting;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

/// <summary>Retains the original selected collector cohort, native policy and case ledger until actual settlement.</summary>
internal sealed class NativeCoverageClusterFixtureOwner
{
    private readonly NativeCoverageClusterFixtureContext context;
    private readonly ClusterFixtureSourceImage sourceImage;
    private readonly IOptions<NativeCoverageExecutionOptions> options;
    private readonly HashSet<NativeCoverageClusterCaseIdentity> executedCases = [];
    private readonly Lock caseGate = new();
    private IReadOnlyDictionary<string, ContainerRuntimeInspection>? startedNodes;
    private IReadOnlyDictionary<string, string>? startedNames;

    private NativeCoverageClusterFixtureOwner(NativeCoverageClusterFixtureContext context,
        ClusterFixtureSourceImage sourceImage, IOptions<NativeCoverageExecutionOptions> options)
    {
        this.context = context;
        this.sourceImage = sourceImage;
        this.options = options;
    }

    internal string Root => Path.Combine(context.FixtureRoot, NativeCoverageClusterFixtureProtocol.StorageDirectoryName);

    internal static async Task<NativeCoverageClusterFixtureOwner?> CreateAsync(string fixtureId, CancellationToken token)
    {
        var execution = NativeCoverageClusterFixtureOptions.Read();
        if (execution is null)
        {
            return null;
        }
        var context = await NativeCoverageClusterFixtureContextReader.ReadAsync(fixtureId, execution, token)
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
        var startupPoll = NativeCoverageClusterFixtureOptions.RequiredEnvironment(
            NativeCoverageClusterFixtureProtocol.StartupPollEnvironment);
        return NativeCoverageClusterFixtureArguments.Create(context, Root, startupPoll);
    }

    internal Task VerifyBeforeStartAsync(DistributedApplication app, CancellationToken token)
        => NativeCoverageClusterImageIdentity.VerifyBeforeStartAsync(app, context, token);

    internal async Task VerifyStartedAsync(IReadOnlyDictionary<string, string> names, CancellationToken token)
    {
        var retainedNames = new Dictionary<string, string>(names, StringComparer.Ordinal);
        var verified = await NativeCoverageClusterImageIdentity.VerifyStartedAsync(retainedNames, context, token)
            .ConfigureAwait(false);
        startedNames = retainedNames;
        startedNodes = verified;
    }

    internal void RegisterCase<TCase>(string methodName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        var className = typeof(TCase).FullName
            ?? throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidRun);
        var identity = new NativeCoverageClusterCaseIdentity(className, methodName, methodName);
        lock (caseGate)
        {
            if (!context.SelectedCases.Contains(identity) || !executedCases.Add(identity))
            {
                throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidRun);
            }
        }
    }

    internal NativeCoverageCleanupDeadline CreateCleanupDeadline() => new(options.Value.ApplicationCleanupTimeout);

    internal async Task<IReadOnlyDictionary<string, ContainerRuntimeInspection>?> VerifyStoppedAsync(
        NativeCoverageCleanupDeadline deadline, CancellationToken token)
    {
        if (startedNodes is null)
        {
            return null;
        }
        var names = startedNames
            ?? throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        return await deadline.WaitAsync(() => NativeCoverageClusterImageIdentity.VerifyStoppedAsync(
            names, startedNodes, context, token)).ConfigureAwait(false);
    }

    internal Task PublishReceiptAsync(IReadOnlyDictionary<string, ContainerRuntimeInspection> stoppedNodes,
        CancellationToken token)
    {
        NativeCoverageClusterCaseIdentity[] executed;
        lock (caseGate)
        {
            executed = executedCases.ToArray();
        }
        return NativeCoverageClusterFixtureReceiptWriter.WriteAsync(context, sourceImage, executed,
            stoppedNodes, options, token);
    }
}
