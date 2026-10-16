using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

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
        Func<int, IGrainPartitionMovementSealedOperationObserver>? observerFactory = null,
        Func<int, IDatabaseAuthenticationExecutionObserver>? authenticationObserverFactory = null,
        Func<int, IGrainRequestPhaseObserver>? phaseObserverFactory = null,
        IGrainRequestPurposeObserver? purposeObserver = null)
    {
        this.settings = settings;
        this.localhost = localhost;
        observers = new(observerFactory, authenticationObserverFactory, phaseObserverFactory, purposeObserver);
    }

    internal ControlledPartitionMovementNativeHost Source => nodes[SourceIndex];
    internal ControlledPartitionMovementNativeHost Target => nodes[DestinationIndex];
    internal RegisteredPhysicalOwnerV1 Control => settings.Control;
    internal RegisteredPhysicalOwnerV1 Destination => settings.Destination;
    internal IReadOnlyList<ControlledPartitionMovementNativeHost> Nodes => nodes;
    internal string Root => settings.Root;

    internal static Task<ControlledPartitionMovementNativeHosts> CreateAsync(CancellationToken cancellationToken)
        => ControlledPartitionMovementNativeHostFactory.CreateAsync(null, cancellationToken);

    internal static Task<ControlledPartitionMovementNativeHosts> CreateAsync(
        Func<int, IGrainPartitionMovementSealedOperationObserver> observerFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observerFactory);
        return ControlledPartitionMovementNativeHostFactory.CreateAsync(observerFactory, cancellationToken);
    }

    internal static Task<ControlledPartitionMovementNativeHosts> CreateWithAuthenticationObserverAsync(
        Func<int, IDatabaseAuthenticationExecutionObserver> observerFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observerFactory);
        return ControlledPartitionMovementNativeHostFactory.CreateAsync(null, observerFactory, null, null, null, null, null, cancellationToken);
    }

    internal static Task<ControlledPartitionMovementNativeHosts> CreateWithPhaseObserverAsync(
        Func<int, IGrainRequestPhaseObserver> observerFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observerFactory);
        return ControlledPartitionMovementNativeHostFactory.CreateAsync(null, null, observerFactory, null, null, null, null, cancellationToken);
    }

    internal static Task<ControlledPartitionMovementNativeHosts> CreateWithMaximumBatchBytesAsync(
        int maximumBatchBytes, CancellationToken cancellationToken)
    {
        return ControlledPartitionMovementNativeHostFactory.CreateAsync(null, null, null, maximumBatchBytes, null, null, null, cancellationToken);
    }

    internal static Task<ControlledPartitionMovementNativeHosts> CreateWithRoutingAsync(
        IOptions<GrainRoutingOptions> routingOptions, Func<int, IGrainRequestPhaseObserver> observerFactory,
        IGrainRequestPurposeObserver? purposeObserver,
        Action<int, ControlledPartitionMovementNativeHost>? beforeStart, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routingOptions);
        ArgumentNullException.ThrowIfNull(observerFactory);
        return ControlledPartitionMovementNativeHostFactory.CreateAsync(null, null, observerFactory,
            null, routingOptions, purposeObserver, beforeStart, cancellationToken);
    }

    internal void RetainCreationFailure(Exception original) => creationFailure = original;

    internal async Task CreateNodesAsync(CancellationToken startupCancellation)
    {
        observers.Prepare();
        for (var index = SourceIndex; index < ControlledPartitionMovementNativeHostEndpoints.OwnerCount; index++)
        {
            nodes.Add(await ControlledPartitionMovementNativeHost.CreateAsync(settings.Arguments(index), localhost,
            observers.Sealed(index), observers.Authentication(index), phaseObserver: observers.Phase(index), purposeObserver: observers.Purpose,
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
