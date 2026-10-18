using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed partial class ControlledPartitionMovementNativeHosts : IAsyncDisposable
{
    private const int SourceIndex = 0;
    private const int DestinationIndex = ControlledPartitionMovementNativeHostEndpoints.GroupSize;
    private readonly ControlledPartitionMovementNativeHostSettings settings;
    private readonly NativeLocalhostClusteringOptions localhost;
    private List<ControlledPartitionMovementNativeHost> nodes = [];
    private readonly Lock lifecycle = new();
    private Task? restart;
    private Task? stopped;
    private bool retainRoots;
    private readonly ControlledPartitionMovementNativeObservers observers;
    private Task? shutdown;
    private Exception? creationFailure;

    internal ControlledPartitionMovementNativeHosts(ControlledPartitionMovementNativeHostSettings settings,
        NativeLocalhostClusteringOptions localhost,
        Func<int, IDatabaseAuthenticationExecutionObserver>? authenticationObserverFactory = null,
        Func<int, IGrainRequestPhaseObserver>? phaseObserverFactory = null,
        IGrainRequestPurposeObserver? purposeObserver = null)
    {
        this.settings = settings;
        this.localhost = localhost;
        observers = new(authenticationObserverFactory, phaseObserverFactory, purposeObserver);
    }

    internal ControlledPartitionMovementNativeHost Source => nodes[SourceIndex];
    internal ControlledPartitionMovementNativeHost Target => nodes[DestinationIndex];
    internal RegisteredPhysicalOwnerV1 Control => settings.Control;
    internal RegisteredPhysicalOwnerV1 Destination => settings.Destination;
    internal IReadOnlyList<ControlledPartitionMovementNativeHost> Nodes => nodes;
    internal string Root => settings.Root;

    internal static Task<ControlledPartitionMovementNativeHosts> CreateAsync(CancellationToken cancellationToken)
        => ControlledPartitionMovementNativeHostFactory.CreateAsync(cancellationToken);

    internal static Task<ControlledPartitionMovementNativeHosts> CreateWithAuthenticationObserverAsync(
        Func<int, IDatabaseAuthenticationExecutionObserver> observerFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observerFactory);
        return ControlledPartitionMovementNativeHostFactory.CreateAsync(observerFactory, null, null, null, null, null, null, cancellationToken);
    }

    internal static Task<ControlledPartitionMovementNativeHosts> CreateWithPhaseObserverAsync(
        Func<int, IGrainRequestPhaseObserver> observerFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observerFactory);
        return ControlledPartitionMovementNativeHostFactory.CreateAsync(null, observerFactory, null, null, null, null, null, cancellationToken);
    }

    internal static Task<ControlledPartitionMovementNativeHosts> CreateWithMaximumBatchBytesAsync(
        int maximumBatchBytes, CancellationToken cancellationToken)
    {
        return ControlledPartitionMovementNativeHostFactory.CreateAsync(null, null, maximumBatchBytes, null, null, null, null, cancellationToken);
    }

    internal void RetainCreationFailure(Exception original) => creationFailure = original;

    internal Task CreateNodesAsync(CancellationToken startupCancellation)
        => CreateNodesAsync(null, startupCancellation);

    internal async Task CreateNodesAsync(Action<int, IServiceCollection, NodeOptions>? configureServices,
        CancellationToken startupCancellation)
    {
        observers.Prepare();
        var configureNode = configureServices;
        for (var index = SourceIndex; index < ControlledPartitionMovementNativeHostEndpoints.OwnerCount; index++)
        {
            var nodeIndex = index;
            nodes.Add(await ControlledPartitionMovementNativeHost.CreateAsync(settings.Arguments(index), localhost,
            observers.Authentication(index), phaseObserver: observers.Phase(index), purposeObserver: observers.Purpose,
                configureServices: configureNode is null ? null : (services, node) => configureNode(nodeIndex, services, node),
                startupCancellation: startupCancellation).ConfigureAwait(false));
        }
    }

    internal static async Task JoinOriginalAsync(Task original)
    {
        try
        {
            await original.ConfigureAwait(false);
        }
        catch (Exception primary)
        {
            throw original.Exception ?? new AggregateException(primary);
        }
    }
}
