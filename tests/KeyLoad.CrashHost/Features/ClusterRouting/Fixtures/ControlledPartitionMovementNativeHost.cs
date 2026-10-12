using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed partial class ControlledPartitionMovementNativeHost : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly Lock lifecycle = new();
    private Task? shutdown;
    private Task? applicationDisposal;
    private Task? partitionDisposal;
    private bool joined;

    private ControlledPartitionMovementNativeHost(WebApplication application)
    {
        this.application = application;
        Runtime = application.Services.GetRequiredService<ServerRuntimeOptions>();
        Clock = application.Services.GetRequiredService<TimeProvider>();
        Partition = application.Services.GetRequiredService<PartitionHost>();
        Silo = application.Services.GetRequiredService<OrleansNode>();
    }

    internal static async Task<ControlledPartitionMovementNativeHost> CreateAsync(string[] arguments,
        NativeLocalhostClusteringOptions localhostClustering,
        IGrainPartitionMovementSealedOperationObserver? sealedObserver = null)
    {
        var application = ServerConfiguration.Build(arguments, sealedObserver: sealedObserver, localhostClustering: localhostClustering);
        try
        { return new(application); }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(() => application.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal ServerRuntimeOptions Runtime { get; }
    internal TimeProvider Clock { get; }
    internal PartitionHost Partition { get; }
    internal OrleansNode Silo { get; }
    internal DatabaseEngine Database => Partition.Database;
    internal IAtomicStore Store => Database.Store;
    internal IDurableReplicaLog Log => Partition.Materializer.Log;
    internal string CallerSiloAddress => Silo.Discovery?.Read().SiloAddress
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
    internal PartitionMovementGrainServiceClient NativeSender => Silo.BorrowPartitionMovementTransport();
    internal bool IsJoined => Volatile.Read(ref joined);

    internal PartitionMovementTransportOwner BorrowTransport()
        => new(Runtime.Node, Silo, Partition, Runtime.Membership, Clock);

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        await Partition.Coordinator.StartAsync(cancellationToken).ConfigureAwait(false);
        await application.StartAsync(cancellationToken).ConfigureAwait(false);
        await Silo.StartAsync(cancellationToken).ConfigureAwait(false);
        await Silo.WaitForPhysicalOwnerRegistrationAsync(cancellationToken).ConfigureAwait(false);
    }
}
