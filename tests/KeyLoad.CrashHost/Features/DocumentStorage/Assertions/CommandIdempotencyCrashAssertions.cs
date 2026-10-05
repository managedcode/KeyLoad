using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal static class CommandIdempotencyCrashAssertions
{
    internal static CommitReceipt RequireReceipt(OperationResult result) => result.Get<CommitReceipt>();

    internal static void RequireInitialReceipt(CommitReceipt receipt)
    {
        if (receipt.CommandId != CommandIdempotencyCrashContract.CommandId || receipt.Mutations.Length != 3
            || !MatchesMutation(receipt.Mutations[0], "putDocument", CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.DocumentId, 1)
            || !MatchesMutation(receipt.Mutations[1], "appendEvents", CommandIdempotencyCrashContract.StreamSet, CommandIdempotencyCrashContract.StreamId, 1)
            || !MatchesMutation(receipt.Mutations[2], "enqueue", CommandIdempotencyCrashContract.Queue, CommandIdempotencyCrashContract.MessageId, 1))
        {
            throw new InvalidOperationException("The initial batch receipt did not contain the exact three expected effects.");
        }
    }

    internal static void RequireSameReceipt(CommitReceipt actual, CommitReceipt expected)
    {
        if (!NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(expected)))
        {
            throw new InvalidOperationException("The replayed command receipt changed from its original bytes.");
        }
    }

    internal static void AssertCanonicalEffects(DatabaseEngine database, ZoneTreeStore store,
        CommitReceipt expectedReceipt, long expectedTail, long seedTail)
    {
        var durable = database.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            CommandIdempotencyCrashContract.Partition, CrashFixtureValues.Principal, CommandIdempotencyCrashContract.CommandId)))?.Result.Get<CommitReceipt>()
            ?? throw new InvalidOperationException("The command outcome disappeared.");
        RequireSameReceipt(durable, expectedReceipt);
        RequireDocument(database.GetDocument(CrashFixtureValues.Principal,
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.DocumentId)),
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.DocumentId),
            1, CommandIdempotencyCrashContract.DocumentJson);
        var page = database.ReadStream(CrashFixtureValues.Principal,
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.StreamSet, CommandIdempotencyCrashContract.StreamId), limit: 2);
        RequireEvent(page);
        var message = database.InspectMessage(CrashFixtureValues.Principal,
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.Queue), CommandIdempotencyCrashContract.MessageId);
        RequireMessage(message);
        if (database.GetOutboxStatus(CrashFixtureValues.Principal, CommandIdempotencyCrashContract.Partition).Head.Tail != expectedTail)
        {
            throw new InvalidOperationException("The command retry changed the committed outbox tail.");
        }
        RequireOutbox(store, expectedReceipt, seedTail);
    }

    internal static void AssertRetriesAndConflict(DatabaseEngine database, ZoneTreeStore store,
        ReplicatedOperation operation, CommitReceipt expected, long committedTail, long seedTail)
    {
        for (var attempt = 0; attempt < CommandIdempotencyCrashContract.RetryCount; attempt++)
        {
            RequireSameReceipt(RequireReceipt(database.Apply(operation)), expected);
            if (database.GetOutboxStatus(CrashFixtureValues.Principal, CommandIdempotencyCrashContract.Partition)
                .Head.Tail != committedTail)
            {
                throw new InvalidOperationException("A matching command retry appended an outbox effect.");
            }
        }
        var changed = CommandIdempotencyCrashData.CreateChangedOperation(operation);
        if (database.Apply(changed).Error != ErrorCode.Conflict)
        {
            throw new InvalidOperationException("Changed command content did not return Conflict.");
        }
        AssertCanonicalEffects(database, store, expected, committedTail, seedTail);
    }

    internal static void RequireFollowUp(CommitReceipt receipt)
    {
        if (receipt.CommandId != CommandIdempotencyCrashContract.FollowUpCommandId || receipt.Mutations.Length != 1
            || !MatchesMutation(receipt.Mutations[0], "putDocument",
            CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.FollowUpDocumentId, 1))
        {
            throw new InvalidOperationException("The healthy follow-up command did not commit its single document effect.");
        }
    }

    internal static void RequireDocument(DocumentResult? document, EntityRef expectedReference, long revision, string json)
    {
        if (document is null || document.Reference != expectedReference || document.Revision != revision
            || !string.Equals(document.Json, json, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The canonical document effect did not match the expected revision and content.");
        }
    }

    private static void RequireEvent(StreamPage page)
    {
        if (page.Stream != new StreamRef(CommandIdempotencyCrashContract.Partition,
                CommandIdempotencyCrashContract.StreamSet, CommandIdempotencyCrashContract.StreamId)
            || page.Head.TailRevision != 1 || page.Head.FirstAvailableRevision != 1 || page.Head.Generation != 1
            || page.Events.Length != 1 || page.HasMore || page.Events[0].Stream.Generation != 1 || page.Events[0].Revision != 1
            || page.Events[0].EventSequence != 1 || page.Events[0].Data.EventId != CommandIdempotencyCrashContract.EventId
            || page.Events[0].Data.EventType != CommandIdempotencyCrashContract.EventType
            || page.Events[0].Data.PayloadJson != CommandIdempotencyCrashContract.EventPayloadJson
            || page.Events[0].Data.HeadersJson != "{}" || page.Events[0].Data.SchemaVersion != 1
            || page.Events[0].Data.CorrelationId is not null || page.Events[0].Data.CausationId is not null)
        {
            throw new InvalidOperationException("The canonical event stream did not contain exactly the committed event identity.");
        }
    }

    private static void RequireMessage(MessageInspection? message)
    {
        if (message is null || message.Metadata.State != MessageState.Ready || message.Metadata.Id != CommandIdempotencyCrashContract.MessageId
            || message.Metadata.Attempts != 0 || message.Metadata.StateVersion != 1
            || message.PayloadJson != CommandIdempotencyCrashContract.QueuePayloadJson || message.HeadersJson != "{}")
        {
            throw new InvalidOperationException("The canonical queue message did not match the committed ready message.");
        }
    }

    internal static bool SameMutationReceipt(MutationReceipt left, MutationReceipt right)
        => left.Kind == right.Kind && left.Resource == right.Resource && left.Id == right.Id && left.Revision == right.Revision;

    private static bool MatchesMutation(MutationReceipt actual, string kind, string resource, string id, long revision)
        => actual.Kind == kind && actual.Resource == resource && actual.Id == id && actual.Revision == revision;

    private static void RequireOutbox(ZoneTreeStore store, CommitReceipt receipt, long seedTail)
    {
        var entries = store.Read(view => Enumerable.Range(1, receipt.Mutations.Length)
            .Select(offset => view.GetRecord<OutboxEntry>(KeySpace.Partition(CommandIdempotencyCrashContract.TailKeySpace,
                CommandIdempotencyCrashContract.Partition, seedTail + offset))).ToArray());
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (entry is null || entry.Ordinal != index || entry.Commit != receipt.Token
                || !SameMutationReceipt(entry.Receipt, receipt.Mutations[index]) || entry.Sequence != seedTail + index + 1)
            {
                throw new InvalidOperationException("The canonical outbox did not retain the exact ordered batch effects.");
            }
        }
        if (entries[0]!.Mutation is not PutDocument || entries[1]!.Mutation is not AppendEvents
            || entries[2]!.Mutation is not EnqueueMessage)
        {
            throw new InvalidOperationException("The canonical outbox mutation sequence changed.");
        }
    }
}
