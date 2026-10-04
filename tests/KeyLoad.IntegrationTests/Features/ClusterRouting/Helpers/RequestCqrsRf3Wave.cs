using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class RequestCqrsRf3Wave : IAsyncDisposable
{
    private readonly string dataRoot;
    private readonly ContainerRuntimeControl runtime;
    private DistributedApplication? application;

    private RequestCqrsRf3Wave(string dataRoot, DistributedApplication application,
        ContainerRuntimeControl runtime)
    { this.dataRoot = dataRoot; this.application = application; this.runtime = runtime; }

    internal DistributedApplication App => application ?? throw new ObjectDisposedException(nameof(RequestCqrsRf3Wave));

    internal static async Task<RequestCqrsRf3Wave> StartAsync(string dataRoot,
        IReadOnlyDictionary<string, string> images, bool configureCohort, bool requireHealthy,
        CancellationToken cancellationToken)
    {
        var args = CreateArguments(dataRoot, images, configureCohort);
        DistributedApplication? app = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RequestCqrsRf3Protocol.WaveDeadline);
        try
        {
            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(args, deadline.Token)
                .ConfigureAwait(false);
            builder.Services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Warning);
            });
            var containers = ContainerNames(builder);
            app = await builder.BuildAsync(deadline.Token).ConfigureAwait(false);
            await RequestCqrsRf3ImageProof.VerifyModelAsync(app, images, deadline.Token).ConfigureAwait(false);
            await app.StartAsync(deadline.Token).ConfigureAwait(false);
            await WaitForNodesAsync(app, requireHealthy, deadline.Token).ConfigureAwait(false);
            var runtime = new ContainerRuntimeControl(app, containers,
                ClusterFixtureDiagnostics.FindRepositoryRoot().FullName);
            return new(dataRoot, app, runtime);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (app is not null)
            { await CompleteAsync(app, failures).ConfigureAwait(false); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal Task KillAsync(string node, CancellationToken cancellationToken)
        => runtime.KillAsync(node, RequestCqrsRf3Protocol.FollowerLossScenario, cancellationToken);

    internal Task RestartAsync(string node, CancellationToken cancellationToken)
        => runtime.RestartAsync(node, cancellationToken);

    internal async Task StopAsync()
    {
        var owned = application;
        if (owned is null)
        { return; }
        var failures = new List<Exception>();
        await CompleteAsync(owned, failures).ConfigureAwait(false);
        if (failures.Count == 0)
        {
            ServerFailureObserver.Observe(() => AssertNodeLocksReleased(dataRoot), failures);
            application = null;
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private static string[] CreateArguments(string dataRoot, IReadOnlyDictionary<string, string> images,
        bool configureCohort)
    {
        var args = new List<string>
        {
            RequestCqrsRf3Protocol.DataRootPrefix + dataRoot,
            RequestCqrsRf3Protocol.Ephemeral
        };
        if (configureCohort)
        {
            args.Add(RequestCqrsRf3Protocol.CohortEnabled);
            foreach (var node in new[] { RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2, RequestCqrsRf3Protocol.Node3 })
            { args.Add(RequestCqrsRf3Protocol.VoterPrefix + node + "=" + images[node]); }
        }
        return [.. args];
    }

    private static async Task WaitForNodesAsync(DistributedApplication app, bool requireHealthy,
        CancellationToken cancellationToken)
    {
        foreach (var node in new[] { RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2, RequestCqrsRf3Protocol.Node3 })
        {
            if (requireHealthy)
            { await app.ResourceNotifications.WaitForResourceHealthyAsync(node, cancellationToken).ConfigureAwait(false); }
            else
            {
                await app.ResourceNotifications.WaitForResourceAsync(node,
                    resource => resource.Snapshot.State?.Text == KnownResourceStates.Running
                        || resource.Snapshot.State?.Text == KnownResourceStates.FailedToStart,
                    cancellationToken).ConfigureAwait(false);
                if (!app.ResourceNotifications.TryGetCurrentState(node, out var state)
                    || state?.Snapshot.State?.Text != KnownResourceStates.Running)
                { throw new InvalidOperationException("An expected mixed-protocol Aspire node did not reach Running."); }
            }
        }
    }

    private static Dictionary<string, string> ContainerNames(IDistributedApplicationTestingBuilder builder)
        => builder.Resources.OfType<ContainerResource>()
            .Where(resource => IsNode(resource.Name))
            .ToDictionary(resource => resource.Name,
                resource => resource.Annotations.OfType<ContainerNameAnnotation>().Single().Name,
                StringComparer.Ordinal);

    private static async Task CompleteAsync(DistributedApplication app, List<Exception> failures)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline);
        await ServerFailureObserver.ObserveAsync(() => app.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
    }

    private static void AssertNodeLocksReleased(string root)
    {
        foreach (var node in new[] { RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2, RequestCqrsRf3Protocol.Node3 })
        {
            var directory = Path.Combine(root, node);
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, "node.owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, "database", "owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, "replica", "owner.lock"));
        }
    }

    private static bool IsNode(string name) => name is RequestCqrsRf3Protocol.Node1
        or RequestCqrsRf3Protocol.Node2 or RequestCqrsRf3Protocol.Node3;
}
