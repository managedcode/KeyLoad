using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.ResourceExecution;
using KeyLoad.UnitTests.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Messaging;

internal static class CompetingInboxAssertions
{
    internal static async Task ResultsAsync(DatabaseEngine database, ZoneTreeStore store, CompetingInboxState state)
    {
        await SubscriptionOldCutAssertions.ReceiptAsync(state.Receipt, state.Competitors[state.Winner].CommandId,
            store, state.Partition, state.EffectPosition,
            new("putDocument", CompetingInboxState.Documents, "effect", 1),
            new("appendEvents", CompetingInboxState.Streams, "effect", 1),
            new("enqueue", CompetingInboxState.Output, "effect", 1), new("Ack", CompetingInboxState.Queue, "input", 3));
        for (var index = 0; index < state.Results.Length; index++)
        {
            var actual = state.Results[index];
            if (state.Equivalent || index == state.Winner)
            {
                await SubscriptionOldCutAssertions.SameAsync(actual.Get<CommitReceipt>(), state.Receipt);
                foreach (var mutation in actual.Get<CommitReceipt>().Mutations)
                {
                    await Assert.That(mutation.CompositionReferences).IsEmpty();
                }
            }
            else
            {
                await Assert.That(Errors.Code(actual.Problem)).IsEqualTo(ErrorCode.Conflict);
                await Assert.That(actual.Problem?.Detail).IsEqualTo("The inbox input was already completed with different effects.");
                await Assert.That(actual.Value).IsNull();
            }
        }
        await ModelAsync(database, state);
    }
    internal static async Task ModelAsync(DatabaseEngine database, CompetingInboxState state)
    {
        var json = state.Json(state.Winner);
        await SubscriptionOldCutAssertions.SameAsync(database.GetDocument("root", new(state.Partition,
            CompetingInboxState.Documents, "effect")), new DocumentResult(
                state.DocumentReference, 1, json, false, []));
        var stream = database.ReadStream("root", new ReadStreamRequest(new(state.Partition, CompetingInboxState.Streams, "effect")));
        await Assert.That(stream.Stream).IsEqualTo(new StreamRef(state.Partition, CompetingInboxState.Streams, "effect"));
        await Assert.That(stream.Head).IsEqualTo(new StreamHead(1, 1, 1));
        await Assert.That(stream.HasMore).IsFalse();
        await Assert.That(stream.Cursor).IsNull();
        await Assert.That(stream.CutPosition > 0).IsTrue();
        await Assert.That(stream.SnapshotCutPosition).IsEqualTo(stream.CutPosition);
        var record = await Assert.That(stream.Events).HasSingleItem();
        await Assert.That(record).IsEqualTo(new EventRecord(new(state.Partition, CompetingInboxState.Streams, "effect"), 1, 1, new("effect", "Completed", json), state.Time));
        await SubscriptionOldCutAssertions.SameAsync(database.InspectMessage("root", state.Lane, "input"),
            new MessageInspection(new("input", MessageState.Acked, 1, 3, 1, null, null, LeaseVersion: 1), null, null));
        await SubscriptionOldCutAssertions.SameAsync(database.InspectMessage("root", new(state.Partition, CompetingInboxState.Output), "effect"),
            new MessageInspection(new("effect", MessageState.Ready, 0, 1, 1, null, null), json, "{}"));
        await CompetingInboxOutbox.RequireAsync(database, state);
    }
    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store, CompetingInboxState state)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = ReadCutStoreStateAssertions.SnapshotIdentity(store);
        for (var index = 0; index < state.Competitors.Length; index++)
        {
            var operation = state.Competitors[index];
            var original = state.Results[index];
            var raw = store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(state.Partition, "root", operation.CommandId)))
                ?? throw new InvalidOperationException("The competing inbox outcome is absent.");
            var replay = database.Apply(operation);
            if (Errors.Code(original.Problem) is null)
            {
                await ReplayResultAssertions.Same<CommitReceipt>(replay, original);
            }
            else
            {
                await Assert.That(OrleansSerialization.Serialize(replay).AsSpan()
                    .SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
                await Assert.That(replay.Value).IsNull();
                await Assert.That(original.Value).IsNull();
            }
            await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(state.Partition,
                "root", operation.CommandId)))!.AsSpan().SequenceEqual(raw)).IsTrue();
            await SubscriptionOldCutAssertions.ConservedAsync(store, bytes, position, identity);
        }
        await ModelAsync(database, state);
    }
}
