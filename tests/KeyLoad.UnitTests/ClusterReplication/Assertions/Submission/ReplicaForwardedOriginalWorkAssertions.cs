using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardedOriginalWorkProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardedOriginalWorkAssertions
{
    internal static async Task SameOriginalAsync(ReplicaForwardedOriginalWorkObserved observed)
    {
        await Assert.That(observed.CreatedReadBytes).IsEqualTo(P.NoReadBytes);
        await Assert.That(observed.BeforeExecuteReadBytes > P.NoReadBytes).IsTrue();
        await Assert.That(observed.SameBudget && observed.SameBinding && observed.SameLedger).IsTrue();
    }

    internal static async Task JoinedAsync(ReplicaForwardedOriginalWorkObserved observed, CancellationToken token)
    {
        var original = observed.Lease.Settlement;
        var settled = await original.WaitAsync(token);
        await Assert.That(ReferenceEquals(original, observed.Lease.Settlement)).IsTrue();
        await Assert.That(settled.CleanupOrJoinFailure).IsNull();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Task.FromResult(observed.Ledger.RetainedEntries));
    }

    internal static async Task ReceiptAsync(RequestCqrsCohortNativeNode leader, PartitionRef partition,
        Guid commandId, string key, long index, CommitReceipt actual)
    {
        var expected = new CommitReceipt(commandId,
            new(leader.Journal.Database.Store.Identity.Incarnation, partition.AtomicPartitionId, index, P.InitialRevision),
            [new(P.PutKind, P.Collection, key, P.InitialRevision)], DurabilityProfile.QuorumProcessDurable);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var entry = leader.Journal.Log.ReadEntry(index);
        await Assert.That(entry?.Operation?.Id).IsEqualTo(commandId);
        await Assert.That(entry?.Operation?.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(entry?.Operation?.PrincipalId).IsEqualTo(P.Root);
    }

    internal static async Task DocumentsAsync(RequestCqrsCohortNativeNode[] nodes, PartitionRef partition,
        ReplicatedOperation creation, CancellationToken token)
    {
        foreach (var node in nodes)
        {
            var database = node.Journal.Database;
            await EqualAsync<DocumentResult?>(new DocumentResult(Reference(creation, partition, P.Collection, P.Anchor), P.InitialRevision, P.AnchorJson, false, []),
                database.GetDocument(P.Root, new(partition, P.Collection, P.Anchor), cancellationToken: token));
            await EqualAsync<DocumentResult?>(new DocumentResult(Reference(creation, partition, P.Collection, P.Healthy), P.InitialRevision, P.HealthyJson, false, []),
                database.GetDocument(P.Root, new(partition, P.Collection, P.Healthy), cancellationToken: token));
            await Assert.That(database.GetDocument(P.Root, new(partition, P.Collection, P.Cancelled), cancellationToken: token)).IsNull();
        }
    }

    internal static async Task<ReplicatedOperation> ConfigureAsync(RequestCqrsHostedRuntimeFixture fixture,
        RequestCqrsCohortNativeNode[] nodes, string collection, CancellationToken token)
    {
        var partition = fixture.Database.Partition;
        var expected = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
            new(collection, ResourceKind.Collection, partition.TransactionDomainId));
        var id = Guid.NewGuid();
        var configured = fixture.Database.Submit(OperationKind.ConfigureResource, expected, P.Root, id, null, token).Get<ResourceDefinition>();
        await EqualAsync(expected.Definition, configured);
        await ReplicaForwardedOriginalWorkNodes.BarrierAsync(nodes, token);
        var index = fixture.Database.ReadJournalCut().LastIndex;
        var original = nodes.First().Journal.Log.ReadEntry(index)?.Operation
            ?? throw new InvalidOperationException(P.MissingObservation);
        await Assert.That(original.Id).IsEqualTo(id);
        await Assert.That(original.Kind).IsEqualTo(OperationKind.ConfigureResource);
        await Assert.That(original.PrincipalId).IsEqualTo(P.Root);
        await EqualAsync(expected, NativeCommandPayload.Read<ConfigureResourceRequest>(original));
        var lifetime = ResourceLifetimeIdentity.Create(original, expected.TenantId, expected.DatabaseId, expected.Definition);
        foreach (var node in nodes)
        {
            var actual = node.Journal.Database.Store.Read(view => ResourceLifetimeCatalog.Read(view,
                expected.TenantId, expected.DatabaseId, collection, expected.Definition));
            await EqualAsync<ResourceLifetimeV1?>(lifetime, actual);
        }
        return original;
    }

    internal static EntityRef Reference(ReplicatedOperation creation, PartitionRef partition, string collection, string id)
    {
        var request = NativeCommandPayload.Read<ConfigureResourceRequest>(creation);
        var lifetime = ResourceLifetimeIdentity.Create(creation, request.TenantId, request.DatabaseId, request.Definition);
        return new(partition, collection, id)
        {
            Lifetime = new EntityRefLifetimeV1(lifetime.Incarnation.Value)
        };
    }

    internal static async Task DependencyAsync(ReplicaForwardedOriginalWorkObserved observed,
        RequestCqrsCohortNativeNode leader, PartitionRef partition, ReplicatedOperation creation)
    {
        var request = NativeCommandPayload.Read<ConfigureResourceRequest>(creation);
        var lifetime = ResourceLifetimeIdentity.Create(creation, request.TenantId, request.DatabaseId, request.Definition);
        var actual = leader.Journal.Database.Store.Read(view => ResourceLifetimeCatalog.Read(view,
            partition.TenantId, partition.DatabaseId, P.Collection, request.Definition));
        await EqualAsync<ResourceLifetimeV1?>(lifetime, actual);
        await Assert.That(observed.Lease.Dependencies.Count).IsEqualTo(P.SingleDependency);
        await Assert.That((ResourceLifetimeV1?)observed.Lease.Dependencies.Single()).IsEqualTo(lifetime);
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
