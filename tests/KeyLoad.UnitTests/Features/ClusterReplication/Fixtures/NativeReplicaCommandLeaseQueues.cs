using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Replication;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class NativeReplicaCommandLeaseQueues
{
    private readonly List<(ClusterCoordinator Queue, NativeRequestWorkOwner Owner)> owners = [];
    internal readonly Dictionary<string, NativeRequestWorkOwner> Work = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, ClusterCoordinator> Queues = new(StringComparer.Ordinal);

    internal async Task StartAsync(RequestCqrsCohortRuntimeFixture fixture, CancellationToken token)
    {
        foreach (var voter in ReplicaBarrierDeadlineFlow.Voters)
        {
            var journal = fixture.Node(voter).Journal;
            var database = journal.Database;
            var configure = new ConfigureResourceRequest(P.Partition.TenantId, P.Partition.DatabaseId,
                new(NativeReplicaCommandLeaseProtocol.Other, ResourceKind.Collection, P.Partition.TransactionDomainId));
            _ = database.ApplyEmbedded(database.CreateNativeOperation(OperationKind.ConfigureResource, Guid.NewGuid(),
                P.Root, TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(configure)), token).Get<ResourceDefinition>();
            var owner = new NativeRequestWorkOwner(UnitRoutingOptions.Routing());
            var queue = new ClusterCoordinator(journal.Consensus, database, new(UnitAdmissionOptions.Command()),
                TimeProvider.System, journal.ExecutionOptions, UnitAdmissionOptions.Inbox());
            owners.Add((queue, owner));
            var before = ReplicaBarrierDeadlineAssertions.Image(fixture.Node(voter));
            var rejection = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
                NativeReplicaCommandExecution.StartAsync(queue, owner, database, token));
            await Assert.That(rejection!.Code).IsEqualTo(ErrorCode.RecoveryRequired);
            await Assert.That(rejection.Message).IsEqualTo("The native resource work fence is not attached.");
            await ReplicaBarrierDeadlineAssertions.UnchangedAsync(fixture.Node(voter), before);
            var catalog = database.Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view))
                ?? throw new InvalidOperationException("The original native catalog is absent.");
            owner.BindPhysicalOwner(catalog.DefaultShard);
            owner.AttachResourceLifetimeWorkFence(database, journal.Log.State.LastIndex);
            await NativeReplicaCommandExecution.StartAsync(queue, owner, database, token);
            Queues.Add(voter, queue);
            Work.Add(voter, owner);
        }
    }

    internal async Task CloseAsync(List<Exception> failures)
    {
        foreach (var pair in owners.AsEnumerable().Reverse())
        {
            await ServerFailureObserver.ObserveAsync(() => pair.Queue.DisposeAsync().AsTask(), failures);
            await ServerFailureObserver.ObserveAsync(() => pair.Owner.DisposeAsync().AsTask(), failures);
        }
    }
}
