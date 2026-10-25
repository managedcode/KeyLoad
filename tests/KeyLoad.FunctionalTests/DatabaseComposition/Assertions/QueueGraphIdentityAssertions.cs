using System.Text.Json;
using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.DatabaseComposition;

internal static class QueueGraphIdentityAssertions
{
    private const string CollectionKey = "collection";
    private const string IdKey = "id";
    private const string LifetimeKey = "lifetime";
    private const string PartitionKey = "partition";

    internal static async Task RefusesAsync(QueueGraphIdentityState state, DatabaseEngine engine, ZoneTreeStore store)
    {
        var before = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Apply(state.Failed).Get<CommitReceipt>());
        await Assert.That(failure.Code).IsEqualTo(state.FailureCode);
        await Assert.That(failure.Message).IsEqualTo(state.FailureDetail);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before,
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(JsonDefaults.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
        await Assert.That(store.Read(view => view.GetRecord<DocumentRecord>(
            global::KeyLoad.Core.DocumentStorage.DocumentStorageKeys.RecordKey(
                new(state.Fixture.Partition, QueueGraphIdentityState.Collection, QueueGraphIdentityState.Marker))))).IsNull();
        await Assert.That(store.Read(view => view.GetRecord<EdgeRecord>(KeySpace.Partition(
            "edge", state.Fixture.Partition, QueueGraphIdentityState.Graph,
            QueueGraphIdentityState.Prefix + QueueGraphIdentityState.Message)))).IsNull();
        await Assert.That(JsonDefaults.Serialize(engine.InspectMessage(QueueGraphIdentityState.Root,
            new(state.Fixture.Partition, QueueGraphIdentityState.Queue), QueueGraphIdentityState.Message))
            .AsSpan().SequenceEqual(JsonDefaults.Serialize(state.FaultSource ?? state.Source))).IsTrue();
    }

    internal static async Task ModelsAsync(QueueGraphIdentityState state, DatabaseEngine engine,
        ZoneTreeStore store, bool healthy)
    {
        foreach (var expected in new[] { state.First, state.Second })
        {
            var actual = store.Read(view => view.GetRecord<DocumentRecord>(
                global::KeyLoad.Core.DocumentStorage.DocumentStorageKeys.RecordKey(expected.Reference)));
            await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            var publicExpected = new DocumentResult(expected.Reference, 1, expected.Json, false, []);
            await Assert.That(JsonDefaults.Serialize(engine.GetDocument(QueueGraphIdentityState.Root, expected.Reference))
                .AsSpan().SequenceEqual(JsonDefaults.Serialize(publicExpected))).IsTrue();
        }
        var marker = state.MarkerReference;
        var expectedMarker = healthy ? new DocumentRecord(marker, 1, QueueGraphIdentityState.MarkerJson,
            new(), new DateTimeOffset(state.Healthy.Timestamp)) : null;
        await Assert.That(JsonDefaults.Serialize(store.Read(view => view.GetRecord<DocumentRecord>(
            global::KeyLoad.Core.DocumentStorage.DocumentStorageKeys.RecordKey(marker))))
            .AsSpan().SequenceEqual(JsonDefaults.Serialize(expectedMarker))).IsTrue();
        var edge = new EdgeRecord(QueueGraphIdentityState.Prefix + QueueGraphIdentityState.Message, state.BoundFrom,
            state.BoundTo, QueueGraphIdentityState.Label, QueueGraphIdentityState.AttributesJson, 1);
        await Assert.That(JsonDefaults.Serialize(store.Read(view => view.GetRecord<EdgeRecord>(KeySpace.Partition(
            "edge", state.Fixture.Partition, QueueGraphIdentityState.Graph, edge.Id))))
            .AsSpan().SequenceEqual(JsonDefaults.Serialize(healthy ? edge : null))).IsTrue();
        var expectedGraph = new global::KeyLoad.GraphTraversal(healthy ? [state.BoundFrom, state.BoundTo] : [state.BoundFrom], healthy ? [edge] : []);
        await Assert.That(JsonDefaults.Serialize(engine.Traverse(QueueGraphIdentityState.Root, state.Fixture.Partition,
            QueueGraphIdentityState.Graph, state.From)).AsSpan().SequenceEqual(JsonDefaults.Serialize(expectedGraph))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(engine.InspectMessage(QueueGraphIdentityState.Root,
            new(state.Fixture.Partition, QueueGraphIdentityState.Queue), QueueGraphIdentityState.Message))
            .AsSpan().SequenceEqual(JsonDefaults.Serialize(state.Source))).IsTrue();
    }

    internal static async Task HealthyAsync(QueueGraphIdentityState state, DatabaseEngine engine, ZoneTreeStore store)
    {
        var nextPosition = checked(engine.LastApplied + 1);
        var receipt = engine.Apply(state.Healthy).Get<CommitReceipt>();
        var expected = new CommitReceipt(state.Healthy.CommandId, new(store.Identity.Incarnation, state.Fixture.Partition.AtomicPartitionId,
                nextPosition), [new("putDocument", QueueGraphIdentityState.Collection, QueueGraphIdentityState.Marker, 1),
                new("upsertEdge", QueueGraphIdentityState.Graph,
                    QueueGraphIdentityState.Prefix + QueueGraphIdentityState.Message, 1)], store.Identity.Durability, (store.Identity.Durability == DurabilityProfile.Buffered ? 0 : 1));
        await Assert.That(JsonDefaults.Serialize(receipt).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(receipt.Mutations[0].CompositionReferences).IsEmpty();
        await Assert.That(receipt.Mutations[1].CompositionReferences.SequenceEqual([state.BoundFrom, state.BoundTo])).IsTrue();
        state.HealthyReceipt = OrleansSerialization.Serialize(receipt);
        var before = QueueWholeFlowStorage.Bytes(store);
        var replay = engine.Apply(state.Healthy).Get<CommitReceipt>();
        var resolved = engine.ResolveOutcome(state.Healthy).Get<CommitReceipt>();
        await Assert.That(OrleansSerialization.Serialize(replay).AsSpan().SequenceEqual(OrleansSerialization.Serialize(receipt))).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(resolved).AsSpan().SequenceEqual(OrleansSerialization.Serialize(receipt))).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before,
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await ModelsAsync(state, engine, store, healthy: true);
    }

    // Independent golden in the server's link field order; the production link type is not serialized.
    internal static string Payload(QueueGraphLink link) => JsonSerializer.Serialize(new
    {
        from = Reference(link.From),
        to = Reference(link.To),
        label = link.Label,
        attributesJson = link.AttributesJson
    }, JsonDefaults.Options);

    private static Dictionary<string, object> Reference(EntityRef reference)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [PartitionKey] = new
            {
                tenantId = reference.Partition.TenantId,
                databaseId = reference.Partition.DatabaseId,
                transactionDomainId = reference.Partition.TransactionDomainId,
                partitionKey = reference.Partition.PartitionKey,
                atomicPartitionId = reference.Partition.AtomicPartitionId
            },
            [CollectionKey] = reference.Collection,
            [IdKey] = reference.Id
        };
        if (reference.Lifetime is { } lifetime)
        {
            result.Add(LifetimeKey, lifetime);
        }
        return result;
    }
}
