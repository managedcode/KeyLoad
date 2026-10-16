using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed partial class ControlledPartitionMovementNativeHost : IAsyncDisposable
{
    private readonly Lock lifecycle = new();
    private Task? startup;
    private Task? shutdown;
    private Task? applicationDisposal;
    private Task? storageStop;
    private readonly ServerConnectionRegistry connections;
    private readonly PartitionStorageLifecycle storageLifecycle;
    private readonly Func<PartitionHostApplicationOwner> storageOwner;
    private Func<PartitionHost>? partition;
    private readonly Func<OrleansNode> silo;
    private bool joined;

    internal ControlledPartitionMovementNativeHost(WebApplication application, ControlledPartitionMovementNativeObservationWitness nativeObservation)
    {
        Application = application;
        NativeObservation = nativeObservation;
        Runtime = application.Services.GetRequiredService<ServerRuntimeOptions>();
        Clock = application.Services.GetRequiredService<TimeProvider>();
        connections = application.Services.GetRequiredService<ServerConnectionRegistry>();
        var originalSilo = application.Services.GetRequiredService<OrleansNode>();
        silo = () => originalSilo;
        storageLifecycle = application.Services.GetRequiredService<PartitionStorageLifecycle>();
        var originalStorageOwner = application.Services.GetRequiredService<PartitionHostApplicationOwner>();
        storageOwner = () => originalStorageOwner;
    }

    internal ControlledPartitionMovementNativeObservationWitness NativeObservation
    {
        get;
    }

    internal static Task<ControlledPartitionMovementNativeHost> CreateAsync(string[] arguments,
        NativeLocalhostClusteringOptions localhostClustering,
        IGrainPartitionMovementSealedOperationObserver? sealedObserver = null,
        IDatabaseAuthenticationExecutionObserver? authenticationObserver = null,
        Func<IServiceProvider, ILifecycleParticipant<ISiloLifecycle>>? lifecycleParticipantFactory = null,
        IGrainRequestPhaseObserver? phaseObserver = null, IGrainRequestPurposeObserver? purposeObserver = null,
        CancellationToken startupCancellation = default)
        => ControlledPartitionMovementNativeApplicationFactory.CreateAsync(arguments, localhostClustering,
            sealedObserver, authenticationObserver, lifecycleParticipantFactory, phaseObserver, purposeObserver, startupCancellation);

    internal WebApplication Application
    {
        get;
    }
    internal ServerRuntimeOptions Runtime
    {
        get;
    }
    internal TimeProvider Clock
    {
        get;
    }
    internal PartitionHost Partition => partition?.Invoke() ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
    internal OrleansNode Silo => partition is not null
        ? silo() : throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
    internal DatabaseEngine Database => Partition.Database;
    internal IAtomicStore Store => Database.Store;
    internal IDurableReplicaLog Log => Partition.Materializer.Log;
    internal string CallerSiloAddress => Silo.Discovery?.Read().SiloAddress
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
    internal PartitionMovementGrainServiceClient NativeSender => Silo.BorrowPartitionMovementTransport();
    internal bool IsJoined => Volatile.Read(ref joined);

    internal PartitionMovementTransportOwner BorrowTransport()
        => new(Runtime.Node, Silo, Partition, Runtime.Membership, Clock);

    internal Task StartAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(shutdown is not null, this);
            return startup ??= StartCoreAsync(cancellationToken);
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        await Application.StartAsync(cancellationToken).ConfigureAwait(false);
        var opened = storageOwner().Borrow();
        var native = silo();
        ControlledPartitionMovementNativePublication.RequireActive(Application, opened, native);
        partition = () => opened;
        await native.StartAsync(cancellationToken).ConfigureAwait(false);
        await native.WaitForPhysicalOwnerRegistrationAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!native.DatabaseReady)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalShardCatalogFence.NotReady);
        }
    }
}
