using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed class NodeEpochRf3Wave : IAsyncDisposable
{
    private readonly string root;
    private readonly ContainerRuntimeControl runtime;
    private readonly HashSet<string> stoppedNodes = new(StringComparer.Ordinal);
    private DistributedApplication? app;

    private NodeEpochRf3Wave(string root, DistributedApplication app, ContainerRuntimeControl runtime)
    { this.root = root; this.app = app; this.runtime = runtime; }

    internal DistributedApplication App => app ?? throw new ObjectDisposedException(nameof(NodeEpochRf3Wave));

    internal static async Task<NodeEpochRf3Wave> StartAsync(string root, string? priorImage,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var args = new List<string>
        {
            NodeEpochRf3Protocol.DataRootArgument + root,
            NodeEpochRf3Protocol.EphemeralArgument,
            NodeEpochRf3Protocol.SnapshotThresholdArgument
        };
        if (priorImage is not null)
        { args.Add(NodeEpochRf3Protocol.ServerImageArgument + priorImage); }
        DistributedApplication? application = null;
        try
        {
            using var deadlineTimeout = new CancellationTokenSource(NodeEpochRf3Protocol.WaveDeadline, TimeProvider.System);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(args.ToArray(), deadline.Token);
            ConfigureLogging(builder);
            var containerNames = ContainerNames(builder);
            application = await builder.BuildAsync(deadline.Token);
            if (priorImage is null)
            { await ClusterFixtureImageIdentity.VerifyAsync(application, deadline.Token).ConfigureAwait(false); }
            else
            { await NodeEpochRf3ImageProof.VerifyPriorModelAsync(application, priorImage, deadline.Token).ConfigureAwait(false); }
            await application.StartAsync(deadline.Token).ConfigureAwait(false);
            await Task.WhenAll(Enumerable.Range(1, NodeEpochRf3Protocol.NodeCount).Select(number =>
                application.ResourceNotifications.WaitForResourceHealthyAsync(NodeName(number), deadline.Token)));
            var runtime = new ContainerRuntimeControl(application, containerNames,
                ClusterFixtureDiagnostics.FindRepositoryRoot().FullName);
            return new(root, application, runtime);
        }
        catch (Exception startupFailure)
        {
            var cleanupFailures = await CleanupPartialAsync(application).ConfigureAwait(false);
            if (cleanupFailures.Count == 0)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(startupFailure).Throw(); }
            cleanupFailures.Insert(0, startupFailure);
            ServerFailureObserver.ThrowIfAny(cleanupFailures);
            throw;
        }
    }

    internal Task KillAsync(string node, string scenario, CancellationToken cancellationToken)
        => KillTrackedAsync(node, scenario, cancellationToken);

    internal Task RestartAsync(string node, CancellationToken cancellationToken)
        => RestartTrackedAsync(node, cancellationToken);

    internal async Task RestartPendingAsync(CancellationToken cancellationToken)
    {
        foreach (var node in stoppedNodes.ToArray())
        { await RestartTrackedAsync(node, cancellationToken).ConfigureAwait(false); }
    }

    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        var owned = app;
        if (owned is null)
        { return; }
        using var deadlineTimeout = new CancellationTokenSource(NodeEpochRf3Protocol.CleanupDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => owned.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(
            () => owned.DisposeAsync().AsTask().WaitAsync(deadline.Token), failures).ConfigureAwait(false);
        if (failures.Count == 0)
        {
            ServerFailureObserver.Observe(() => AssertNodeLocksReleased(root), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        app = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        { await StopAsync(CancellationToken.None).ConfigureAwait(false); }
    }

    private static async Task<List<Exception>> CleanupPartialAsync(DistributedApplication? application)
    {
        var failures = new List<Exception>();
        if (application is null)
        { return failures; }
        using var deadline = new CancellationTokenSource(NodeEpochRf3Protocol.CleanupDeadline, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(() => application.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(
            () => application.DisposeAsync().AsTask().WaitAsync(deadline.Token), failures).ConfigureAwait(false);
        return failures;
    }

    private async Task KillTrackedAsync(string node, string scenario, CancellationToken cancellationToken)
    {
        await runtime.KillAsync(node, scenario, cancellationToken).ConfigureAwait(false);
        _ = stoppedNodes.Add(node);
    }

    private async Task RestartTrackedAsync(string node, CancellationToken cancellationToken)
    {
        await runtime.RestartAsync(node, cancellationToken).ConfigureAwait(false);
        _ = stoppedNodes.Remove(node);
    }

    private static Dictionary<string, string> ContainerNames(IDistributedApplicationTestingBuilder builder)
    {
        var result = builder.Resources.OfType<ContainerResource>()
            .Where(resource => resource.Name is NodeEpochRf3Protocol.Node1 or NodeEpochRf3Protocol.Node2 or NodeEpochRf3Protocol.Node3)
            .ToDictionary(resource => resource.Name,
                resource => resource.Annotations.OfType<ContainerNameAnnotation>().Single().Name, StringComparer.Ordinal);
        if (result.Count != NodeEpochRf3Protocol.NodeCount)
        { throw new InvalidOperationException("The cold-RF3 Aspire model must contain exactly node1, node2 and node3."); }
        return result;
    }

    private static void ConfigureLogging(IDistributedApplicationTestingBuilder builder)
    {
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService", LogLevel.Critical);
        });
    }

    private static void AssertNodeLocksReleased(string root)
    {
        foreach (var number in Enumerable.Range(1, NodeEpochRf3Protocol.NodeCount))
        {
            var node = Path.Combine(root, NodeName(number));
            AssertExclusive(Path.Combine(node, "node.owner.lock"));
            AssertExclusive(Path.Combine(node, "database", "owner.lock"));
            AssertExclusive(Path.Combine(node, "replica", "owner.lock"));
        }
    }

    private static void AssertExclusive(string path)
        => NodeEpochRf3OfflineFiles.AssertExclusive(path);

    private static string NodeName(int number) => number switch
    {
        1 => NodeEpochRf3Protocol.Node1,
        2 => NodeEpochRf3Protocol.Node2,
        3 => NodeEpochRf3Protocol.Node3,
        _ => throw new ArgumentOutOfRangeException(nameof(number))
    };
}
