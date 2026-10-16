using KeyLoad.Clustering;
using KeyLoad.Server;

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
    private readonly Func<int, IGrainPartitionMovementSealedOperationObserver>? observerFactory;
    private IGrainPartitionMovementSealedOperationObserver[]? sealedObservers;
    private Task? shutdown;
    private Exception? creationFailure;
    private readonly Func<int, IDatabaseAuthenticationExecutionObserver>? authenticationObserverFactory;
    private IDatabaseAuthenticationExecutionObserver[]? authenticationObservers;

    internal ControlledPartitionMovementNativeHosts(ControlledPartitionMovementNativeHostSettings settings,
        NativeLocalhostClusteringOptions localhost,
        Func<int, IGrainPartitionMovementSealedOperationObserver>? observerFactory = null,
        Func<int, IDatabaseAuthenticationExecutionObserver>? authenticationObserverFactory = null)
    {
        this.settings = settings;
        this.localhost = localhost;
        this.observerFactory = observerFactory;
        this.authenticationObserverFactory = authenticationObserverFactory;
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
        return ControlledPartitionMovementNativeHostFactory.CreateAsync(null, observerFactory, cancellationToken);
    }

    internal static Task<ControlledPartitionMovementNativeHosts> CreateWithMaximumBatchBytesAsync(
        int maximumBatchBytes, CancellationToken cancellationToken)
    {
        return ControlledPartitionMovementNativeHostFactory.CreateAsync(null, null, cancellationToken, maximumBatchBytes);
    }

    internal void RetainCreationFailure(Exception original) => creationFailure = original;

    internal async Task CreateNodesAsync(CancellationToken startupCancellation)
    {
        if (observerFactory is not null && sealedObservers is null)
        {
            sealedObservers = new IGrainPartitionMovementSealedOperationObserver[ControlledPartitionMovementNativeHostEndpoints.OwnerCount];
            for (var index = SourceIndex; index < sealedObservers.Length; index++)
            {
                sealedObservers[index] = observerFactory(index) ?? throw new InvalidOperationException(nameof(observerFactory));
            }
        }
        if (authenticationObserverFactory is not null && authenticationObservers is null)
        {
            authenticationObservers = new IDatabaseAuthenticationExecutionObserver[ControlledPartitionMovementNativeHostEndpoints.OwnerCount];
            for (var index = SourceIndex; index < authenticationObservers.Length; index++)
            {
                authenticationObservers[index] = authenticationObserverFactory(index) ?? throw new InvalidOperationException(nameof(authenticationObserverFactory));
            }
        }
        for (var index = SourceIndex; index < ControlledPartitionMovementNativeHostEndpoints.OwnerCount; index++)
        {
            nodes.Add(await ControlledPartitionMovementNativeHost.CreateAsync(settings.Arguments(index), localhost,
            sealedObservers?[index], authenticationObservers?[index], startupCancellation: startupCancellation).ConfigureAwait(false));
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
