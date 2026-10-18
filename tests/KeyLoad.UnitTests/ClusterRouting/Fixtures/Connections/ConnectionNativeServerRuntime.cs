using KeyLoad.Clustering;
using KeyLoad.RecoveryTests.Features.ClusterRouting;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Reuses the existing six actual Server owners: two CLUSTER groups, with original joined shutdown.</summary>
internal sealed class ConnectionNativeServerRuntime : IAsyncDisposable
{
    internal const string NotStarted = "The actual native Server cohort is not started.";
    private const string FailureRoot = "KeyLoad.ConnectionNative.ServerRoot";
    private readonly IGrainRequestPhaseObserver observer;
    private readonly IGrainRequestPurposeObserver? purposeObserver;
    private ControlledPartitionMovementNativeHosts? hosts;
    private CancellationToken originalToken;
    private ConnectionNativeServerInventory? inventory;
    internal Guid ConnectionId { get; } = Guid.NewGuid();
    internal IOptions<GrainRoutingOptions> RoutingOptions
    {
        get;
    }
    internal ConnectionNativeServerEvidence Evidence { get; } = new();
    internal NativeRuntimeTestOptions Timing { get; } = new();
    internal ConnectionNativeServerCluster Cluster
    {
        get;
    }
    internal ConnectionNativeServerDatabase Database
    {
        get;
    }
    internal TimeProvider Clock => Source.Clock;
    internal GrainRequestCodec Codec => Source.Silo.CatalogRequestCodec();
    internal NativeRequestWorkOwner RequestWork => Source.Silo.RequestWork;
    internal bool IsDisposalJoined => hosts is not null && hosts.Nodes.All(node => node.IsJoined);
    internal ControlledPartitionMovementNativeHosts Hosts => hosts
        ?? throw new InvalidOperationException(NotStarted);
    internal Task JoinOwnedResourcesAsync() => Hosts.DisposeAsync().AsTask();
    internal ControlledPartitionMovementNativeHost Source => hosts?.Source
        ?? throw new InvalidOperationException(NotStarted);

    internal ConnectionNativeServerRuntime(GrainRoutingOptions? routing, IGrainRequestPhaseObserver observer,
        IGrainRequestPurposeObserver? purposeObserver = null)
    {
        RoutingOptions = UnitRoutingOptions.Routing(routing);
        this.observer = observer;
        this.purposeObserver = purposeObserver;
        Cluster = new(() => Source);
        Database = new(() => Source, () => originalToken);
    }

    internal async Task InitializeAsync(CancellationToken token)
    {
        originalToken = token;
        using var deadline = new CancellationTokenSource(Timing.StartupTimeout, TimeProvider.System);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        hosts = await ControlledPartitionMovementNativeHostFactory.CreateWithRoutingAsync(RoutingOptions,
            _ => observer, purposeObserver, Evidence.Configure, beforeStart: null, startup.Token).ConfigureAwait(false);
        inventory = new(hosts, Cluster.Client, ConnectionId);
    }

    internal void RegisterConsumer(Guid id) => RequireInventory().RegisterConsumer(id);
    internal Task<HashSet<GrainId>> ActiveConsumersAsync(CancellationToken token)
        => RequireInventory().ReadAsync(token);
    private ConnectionNativeServerInventory RequireInventory() => inventory
        ?? throw new InvalidOperationException(NotStarted);

    internal void RetainRoots() => hosts?.RetainRoots();

    internal void ReportFailure(List<Exception> failures)
    {
        RetainRoots();
        ServerFailureObserver.Observe(() => Evidence.SaveOperationFailure(IsDisposalJoined, failures), failures);
        if (hosts is { } actual)
        {
            foreach (var failure in failures.ToArray())
            {
                ServerFailureObserver.Observe(() => failure.Data[FailureRoot] = actual.Root, failures);
            }
        }
    }

    public ValueTask DisposeAsync() => hosts is { } actual ? actual.DisposeAsync() : ValueTask.CompletedTask;
}
