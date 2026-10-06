using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal static class CommandIdempotencyCrashAssertions
{
    private const int EventReadPageSize = 2;
    private const string EnqueueMutationKind = "enqueue";

    internal static CommitReceipt RequireReceipt(OperationResult result) => result.Get<CommitReceipt>();

    internal static void RequireInitialReceipt(CommitReceipt receipt)
    {
        const int ExpectedMutationCount = 3;
        const int FirstMutationIndex = 0;
        const string RequireInitialReceiptKindText = "putDocument";
        const int InitialRevision = 1;
        const int EventMutationIndex = 1;
        const string RequireInitialReceiptRequireInitialReceiptKindText = "appendEvents";
        const int QueueMutationIndex = 2;
        const string RequireInitialReceiptMessageText = "The initial batch receipt did not contain the exact three expected effects.";

        if (receipt.CommandId != CommandIdempotencyCrashContract.CommandId || receipt.Mutations.Length != ExpectedMutationCount
            || !MatchesMutation(receipt.Mutations[FirstMutationIndex], RequireInitialReceiptKindText, CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.DocumentId, InitialRevision)
            || !MatchesMutation(receipt.Mutations[EventMutationIndex], RequireInitialReceiptRequireInitialReceiptKindText, CommandIdempotencyCrashContract.StreamSet, CommandIdempotencyCrashContract.StreamId, InitialRevision)
            || !MatchesMutation(receipt.Mutations[QueueMutationIndex], EnqueueMutationKind, CommandIdempotencyCrashContract.Queue, CommandIdempotencyCrashContract.MessageId, InitialRevision))
        {
            throw new InvalidOperationException(RequireInitialReceiptMessageText);
        }
    }

    internal static void RequireSameReceipt(CommitReceipt actual, CommitReceipt expected)
    {
        const string RequireSameReceiptMessageText = "The replayed command receipt changed from its original bytes.";

        if (!NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(expected)))
        {
            throw new InvalidOperationException(RequireSameReceiptMessageText);
        }
    }

    internal static void AssertCanonicalEffects(DatabaseEngine database, ZoneTreeStore store,
        CommitReceipt expectedReceipt, long expectedTail, long seedTail)
    {
        const string AssertCanonicalEffectsMessageText = "The command outcome disappeared.";
        const int InitialRevision = 1;
        const string AssertCanonicalEffectsAssertCanonicalEffectsMessageText = "The command retry changed the committed outbox tail.";

        var durable = database.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            CommandIdempotencyCrashContract.Partition, CrashFixtureValues.Principal, CommandIdempotencyCrashContract.CommandId)))?.Result.Get<CommitReceipt>()
            ?? throw new InvalidOperationException(AssertCanonicalEffectsMessageText);
        RequireSameReceipt(durable, expectedReceipt);
        RequireDocument(database.GetDocument(CrashFixtureValues.Principal,
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.DocumentId)),
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.DocumentId),
            InitialRevision, CommandIdempotencyCrashContract.DocumentJson);
        var page = database.ReadStream(CrashFixtureValues.Principal,
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.StreamSet, CommandIdempotencyCrashContract.StreamId), limit: EventReadPageSize);
        RequireEvent(page);
        var message = database.InspectMessage(CrashFixtureValues.Principal,
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.Queue), CommandIdempotencyCrashContract.MessageId);
        RequireMessage(message);
        if (database.GetOutboxStatus(CrashFixtureValues.Principal, CommandIdempotencyCrashContract.Partition).Head.Tail != expectedTail)
        {
            throw new InvalidOperationException(AssertCanonicalEffectsAssertCanonicalEffectsMessageText);
        }
        RequireOutbox(store, expectedReceipt, seedTail);
    }

    internal static void AssertRetriesAndConflict(DatabaseEngine database, ZoneTreeStore store,
        ReplicatedOperation operation, CommitReceipt expected, long committedTail, long seedTail)
    {
        const int FirstReplayAttempt = 0;
        const string AssertRetriesAndConflictMessageText = "A matching command retry appended an outbox effect.";
        const string AssertRetriesAndConflictAssertRetriesAndConflictMessageText = "Changed command content did not return Conflict.";

        for (var attempt = FirstReplayAttempt; attempt < CommandIdempotencyCrashContract.RetryCount; attempt++)
        {
            RequireSameReceipt(RequireReceipt(database.Apply(operation)), expected);
            if (database.GetOutboxStatus(CrashFixtureValues.Principal, CommandIdempotencyCrashContract.Partition)
                .Head.Tail != committedTail)
            {
                throw new InvalidOperationException(AssertRetriesAndConflictMessageText);
            }
        }
        var changed = CommandIdempotencyCrashData.CreateChangedOperation(operation);
        if (database.Apply(changed).Error != ErrorCode.Conflict)
        {
            throw new InvalidOperationException(AssertRetriesAndConflictAssertRetriesAndConflictMessageText);
        }
        AssertCanonicalEffects(database, store, expected, committedTail, seedTail);
    }

    internal static void RequireFollowUp(CommitReceipt receipt)
    {
        const int ExpectedMutationCount = 1;
        const int FirstMutationIndex = 0;
        const string RequireFollowUpKindText = "putDocument";
        const int InitialRevision = 1;
        const string RequireFollowUpMessageText = "The healthy follow-up command did not commit its single document effect.";

        if (receipt.CommandId != CommandIdempotencyCrashContract.FollowUpCommandId || receipt.Mutations.Length != ExpectedMutationCount
            || !MatchesMutation(receipt.Mutations[FirstMutationIndex], RequireFollowUpKindText,
            CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.FollowUpDocumentId, InitialRevision))
        {
            throw new InvalidOperationException(RequireFollowUpMessageText);
        }
    }

    internal static void RequireDocument(DocumentResult? document, EntityRef expectedReference, long revision, string json)
    {
        const string RequireDocumentMessageText = "The canonical document effect did not match the expected revision and content.";

        if (document is null || document.Reference != expectedReference || document.Revision != revision
            || !string.Equals(document.Json, json, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(RequireDocumentMessageText);
        }
    }

    private static void RequireEvent(StreamPage page)
    {
        const int InitialEventRevision = 1;
        const int InitialGeneration = 1;
        const int ExpectedEventCount = 1;
        const int FirstMutationIndex = 0;
        const int InitialEventSequence = 1;
        const string RequireEventComparisonText = "{}";
        const int InitialSchemaVersion = 1;
        const string RequireEventMessageText = "The canonical event stream did not contain exactly the committed event identity.";

        if (page.Stream != new StreamRef(CommandIdempotencyCrashContract.Partition,
                CommandIdempotencyCrashContract.StreamSet, CommandIdempotencyCrashContract.StreamId)
            || page.Head.TailRevision != InitialEventRevision || page.Head.FirstAvailableRevision != InitialEventRevision || page.Head.Generation != InitialGeneration
            || page.Events.Length != ExpectedEventCount || page.HasMore || page.Events[FirstMutationIndex].Stream.Generation != InitialGeneration || page.Events[FirstMutationIndex].Revision != InitialEventRevision
            || page.Events[FirstMutationIndex].EventSequence != InitialEventSequence || page.Events[FirstMutationIndex].Data.EventId != CommandIdempotencyCrashContract.EventId
            || page.Events[FirstMutationIndex].Data.EventType != CommandIdempotencyCrashContract.EventType
            || page.Events[FirstMutationIndex].Data.PayloadJson != CommandIdempotencyCrashContract.EventPayloadJson
            || page.Events[FirstMutationIndex].Data.HeadersJson != RequireEventComparisonText || page.Events[FirstMutationIndex].Data.SchemaVersion != InitialSchemaVersion
            || page.Events[FirstMutationIndex].Data.CorrelationId is not null || page.Events[FirstMutationIndex].Data.CausationId is not null)
        {
            throw new InvalidOperationException(RequireEventMessageText);
        }
    }

    private static void RequireMessage(MessageInspection? message)
    {
        const int NoDeliveryAttempts = 0;
        const int InitialStateVersion = 1;
        const string RequireMessageComparisonText = "{}";
        const string RequireMessageMessageText = "The canonical queue message did not match the committed ready message.";

        if (message is null || message.Metadata.State != MessageState.Ready || message.Metadata.Id != CommandIdempotencyCrashContract.MessageId
            || message.Metadata.Attempts != NoDeliveryAttempts || message.Metadata.StateVersion != InitialStateVersion
            || message.PayloadJson != CommandIdempotencyCrashContract.QueuePayloadJson || message.HeadersJson != RequireMessageComparisonText)
        {
            throw new InvalidOperationException(RequireMessageMessageText);
        }
    }

    internal static bool SameMutationReceipt(MutationReceipt left, MutationReceipt right)
        => left.Kind == right.Kind && left.Resource == right.Resource && left.Id == right.Id && left.Revision == right.Revision;

    private static bool MatchesMutation(MutationReceipt actual, string kind, string resource, string id, long revision)
        => actual.Kind == kind && actual.Resource == resource && actual.Id == id && actual.Revision == revision;

    private static void RequireOutbox(ZoneTreeStore store, CommitReceipt receipt, long seedTail)
    {
        const int FirstOutboxRevision = 1;
        const int IndexInitialValue = 0;
        const int SeedTailIndexStep = 1;
        const string RequireOutboxMessageText = "The canonical outbox did not retain the exact ordered batch effects.";
        const int EntriesFirstIndex = 0;
        const int EntriesSecondIndex = 1;
        const int EntriesComponentIndex = 2;
        const string RequireOutboxRequireOutboxMessageText = "The canonical outbox mutation sequence changed.";

        var entries = store.Read(view => Enumerable.Range(FirstOutboxRevision, receipt.Mutations.Length)
            .Select(offset => view.GetRecord<OutboxEntry>(KeySpace.Partition(CommandIdempotencyCrashContract.TailKeySpace,
                CommandIdempotencyCrashContract.Partition, seedTail + offset))).ToArray());
        for (var index = IndexInitialValue; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (entry is null || entry.Ordinal != index || entry.Commit != receipt.Token
                || !SameMutationReceipt(entry.Receipt, receipt.Mutations[index]) || entry.Sequence != seedTail + index + SeedTailIndexStep)
            {
                throw new InvalidOperationException(RequireOutboxMessageText);
            }
        }
        if (entries[EntriesFirstIndex]!.Mutation is not PutDocument || entries[EntriesSecondIndex]!.Mutation is not AppendEvents
            || entries[EntriesComponentIndex]!.Mutation is not EnqueueMessage)
        {
            throw new InvalidOperationException(RequireOutboxRequireOutboxMessageText);
        }
    }
}
