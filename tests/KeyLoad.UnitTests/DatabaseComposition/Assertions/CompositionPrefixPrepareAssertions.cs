using System.Text.Json;
using KeyLoad.Core.InternalSerialization;
using KeyLoad.Core.StoragePartitions.Contracts;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Messaging;
using ManagedCode.Communication;
using D = KeyLoad.UnitTests.DatabaseComposition.CompositionPrefixPrepareData;

namespace KeyLoad.UnitTests.DatabaseComposition;

internal static class CompositionPrefixPrepareAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));

    internal static async Task ResultAsync(CompositionPrefixPrepareCommand command, Result<object> result)
    {
        await Assert.That(Errors.Code(result.Problem).Value).IsEqualTo(command.Error);
        await Assert.That(result.Problem?.Detail).IsEqualTo(command.SafeDetail);
        if (command.Receipt is { } receipt)
        {
            await EqualAsync(receipt, result.Get<CommitReceipt>());
            await Assert.That(result.Get<CommitReceipt>().Mutations.SelectMany(row => row.CompositionReferences)
                .SequenceEqual(receipt.Mutations.SelectMany(row => row.CompositionReferences))).IsTrue();
        }
        else
        {
            await Assert.That(result.Value).IsNull();
        }
    }

    internal static async Task ReplayAsync(CompositionPrefixPrepareOwner owner)
    {
        var image = QueueWholeFlowStorage.Bytes(owner.Store);
        var cut = owner.Cut();
        var applied = owner.Database.LastApplied;
        var position = owner.Store.Position;
        foreach (var command in owner.Commands)
        {
            await Assert.That(OrleansSerialization.Serialize(command.Original).SequenceEqual(command.OriginalBytes)).IsTrue();
            await Assert.That(OrleansSerialization.Serialize(owner.Retained(command.Original)).SequenceEqual(command.OriginalBytes)).IsTrue();
            await EqualAsync(command.Request, OrleansSerialization.Deserialize<CommandRequest>((command.Original).Value!.Input.Span));
            await ResultAsync(command, owner.Submit(command.Original));
            await ResultAsync(command, owner.Database.ResolveOutcome(command.Original));
            var stored = OutcomeStoreOracle.ReadStored(owner.Store, command.Original)!;
            await Assert.That(stored.Fingerprint).IsEqualTo(DatabaseCommandFingerprint.Compute(command.Original));
            await ResultAsync(command, stored.Result);
        }
        await Assert.That(QueueWholeFlowStorage.Bytes(owner.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(owner.Cut()).IsEqualTo(cut);
        await Assert.That(owner.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(owner.Store.Position).IsEqualTo(position);
    }

    internal static async Task ColdAsync(CompositionPrefixPrepareOwner owner, Func<Task> models)
    {
        await models();
        await ReplayAsync(owner);
        for (var cold = D.Absent; cold < D.ColdCount; cold++)
        {
            var image = QueueWholeFlowStorage.Bytes(owner.Store);
            var cut = owner.Cut();
            await owner.ReopenAsync();
            await Assert.That(QueueWholeFlowStorage.Bytes(owner.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
            await Assert.That(owner.Cut()).IsEqualTo(cut);
            await models();
            await ReplayAsync(owner);
        }
    }

    internal static async Task EdgeAsync(CompositionPrefixPrepareOwner owner, string graph, string id)
    {
        var expected = new EdgeRecord(id, CompositionPrefixPrepareSeed.Bound(owner, D.From, D.First),
            CompositionPrefixPrepareSeed.Bound(owner, D.To, D.Second), D.Label, D.EmptyJson, D.FirstRevision);
        await EqualAsync(expected, owner.Store.Read(view => view.GetRecord<EdgeRecord>(
            KeySpace.Partition("edge", owner.Source.Partition, graph, id))));
    }

    internal static string[] ModelImage(CompositionPrefixPrepareOwner owner)
        => ModelImage(owner.Store, owner.Source.Partition);

    internal static string[] ModelImage(ZoneTreeStore store, PartitionRef partition) => store.Read(view =>
    {
        var rows = new List<string>();
        foreach (var family in new[] { "document", "edge", "adjacency", "message-meta", "message-body",
            "ready", "scheduled", "queue-counters", "outbox", PartitionRecordFamilies.OutboxHead,
            PartitionRecordFamilies.GraphEdgeOwnerVersion, PartitionRecordFamilies.DocumentEpoch,
            PartitionRecordFamilies.Index, PartitionRecordFamilies.Unique, PartitionRecordFamilies.VisibilityEpoch })
        {
            var page = view.Scan(KeySpace.Partition(family, partition), D.ScanBound);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The prefix fixture model image exceeded its declared bound.");
            }
            rows.AddRange(page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)));
        }
        return rows.ToArray();
    });
}
