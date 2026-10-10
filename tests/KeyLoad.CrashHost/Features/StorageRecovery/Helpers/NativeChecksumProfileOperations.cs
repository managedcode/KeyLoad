using KeyLoad.Core;
using KeyLoad.CrashHost.Features.DocumentStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class NativeChecksumProfileOperations
{
    internal static async Task RequireOriginalAsync(string root, DatabaseEngine database, ZoneTreeStore store,
        int appends, bool replay)
    {
        var operation = await CommandIdempotencyCrashData.ReadEvidenceAsync<ReplicatedOperation>(root, CommandIdempotencyCrashContract.CommandEvidenceFile);
        var receipt = await CommandIdempotencyCrashData.ReadEvidenceAsync<CommitReceipt>(root, CommandIdempotencyCrashContract.ReceiptEvidenceFile);
        var tail = await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(root, CommandIdempotencyCrashContract.SeedTailEvidenceFile);
        CommandIdempotencyCrashAssertions.RequireSameReceipt(database.ResolveOutcome(operation).Get<CommitReceipt>(), receipt);
        CommandIdempotencyCrashAssertions.AssertCanonicalEffects(database, store, receipt,
            tail + NativeChecksumProfileProtocol.OriginalEffects + appends, tail);
        if (replay)
        {
            CommandIdempotencyCrashAssertions.AssertRetriesAndConflict(database, store, operation, receipt,
                tail + NativeChecksumProfileProtocol.OriginalEffects + appends, tail);
        }
    }

    internal static async Task AppendAsync(string root, DatabaseEngine database, ZoneTreeStore store, int append)
    {
        var operation = Create(append);
        var receipt = database.Apply(operation).Get<CommitReceipt>();
        RequireReceipt(receipt, append);
        CommandIdempotencyCrashAssertions.RequireSameReceipt(database.Apply(operation).Get<CommitReceipt>(), receipt);
        if (database.Apply(CommandIdempotencyCrashData.CreateChangedOperation(operation)).Error != ErrorCode.Conflict)
        { throw new InvalidOperationException(NativeChecksumProfileProtocol.Invalid); }
        await CommandIdempotencyCrashData.SaveEvidenceAsync(root, CommandFile(append), operation);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(root, ReceiptFile(append), receipt);
        await RequireAppendAsync(root, database, store, append);
    }

    internal static async Task RequireAppendAsync(string root, DatabaseEngine database, ZoneTreeStore store, int append)
    {
        var operation = await CommandIdempotencyCrashData.ReadEvidenceAsync<ReplicatedOperation>(root, CommandFile(append));
        var receipt = await CommandIdempotencyCrashData.ReadEvidenceAsync<CommitReceipt>(root, ReceiptFile(append));
        CommandIdempotencyCrashAssertions.RequireSameReceipt(database.ResolveOutcome(operation).Get<CommitReceipt>(), receipt);
        RequireReceipt(receipt, append);
        var retained = store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(CommandIdempotencyCrashContract.Partition,
            CrashFixtureValues.Principal, CommandId(append))))?.Result.Get<CommitReceipt>()
            ?? throw new InvalidOperationException(NativeChecksumProfileProtocol.Invalid);
        CommandIdempotencyCrashAssertions.RequireSameReceipt(retained, receipt);
        CommandIdempotencyCrashAssertions.RequireDocument(database.GetDocument(CrashFixtureValues.Principal,
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.Collection, DocumentId(append))),
            new(CommandIdempotencyCrashContract.Partition, CommandIdempotencyCrashContract.Collection, DocumentId(append)),
            NativeChecksumProfileProtocol.InitialRevision, Json(append));
        var seedTail = await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(root, CommandIdempotencyCrashContract.SeedTailEvidenceFile);
        RequireOutbox(store, receipt, seedTail + NativeChecksumProfileProtocol.OriginalEffects + append, append);
    }

    private static void RequireOutbox(ZoneTreeStore store, CommitReceipt receipt, long sequence, int append)
    {
        var entry = store.Read(view => view.GetRecord<OutboxEntry>(KeySpace.Partition(CommandIdempotencyCrashContract.TailKeySpace,
            CommandIdempotencyCrashContract.Partition, sequence)));
        if (entry is null || entry.Commit != receipt.Token || entry.Sequence != sequence
            || entry.Ordinal != NativeChecksumProfileProtocol.EmptyRevision
            || !CommandIdempotencyCrashAssertions.SameMutationReceipt(entry.Receipt, receipt.Mutations[NativeChecksumProfileProtocol.EmptyRevision])
            || entry.Mutation is not PutDocument document || document.Json != Json(append)
            || document.Collection != CommandIdempotencyCrashContract.Collection || document.Id != DocumentId(append)
            || document.ExpectedRevision != NativeChecksumProfileProtocol.EmptyRevision)
        { throw new InvalidOperationException(NativeChecksumProfileProtocol.Invalid); }
    }

    private static void RequireReceipt(CommitReceipt receipt, int append)
    {
        if (receipt.CommandId != CommandId(append) || receipt.Mutations.Length != NativeChecksumProfileProtocol.OneAppend)
        { throw new InvalidOperationException(NativeChecksumProfileProtocol.Invalid); }
        var mutation = receipt.Mutations[NativeChecksumProfileProtocol.EmptyRevision];
        if (mutation.Kind != NativeChecksumProfileProtocol.PutDocumentKind || mutation.Resource != CommandIdempotencyCrashContract.Collection
            || mutation.Id != DocumentId(append) || mutation.Revision != NativeChecksumProfileProtocol.InitialRevision)
        { throw new InvalidOperationException(NativeChecksumProfileProtocol.Invalid); }
    }

    private static ReplicatedOperation Create(int append)
    {
        var id = CommandId(append);
        var request = new CommandRequest(id, CommandIdempotencyCrashContract.Partition,
            [new PutDocument(CommandIdempotencyCrashContract.Collection, DocumentId(append), Json(append), NativeChecksumProfileProtocol.EmptyRevision)]);
        return CrashDatabase.Operation(OperationKind.Batch, request, id);
    }

    private static Guid CommandId(int append) => append == NativeChecksumProfileProtocol.OneAppend
        ? CommandIdempotencyCrashContract.FollowUpCommandId : Guid.Parse(NativeChecksumProfileProtocol.SecondCommandText);
    private static string DocumentId(int append) => append == NativeChecksumProfileProtocol.OneAppend
        ? CommandIdempotencyCrashContract.FollowUpDocumentId : NativeChecksumProfileProtocol.SecondDocumentId;
    private static string Json(int append) => append == NativeChecksumProfileProtocol.OneAppend
        ? CommandIdempotencyCrashContract.FollowUpJson : NativeChecksumProfileProtocol.SecondJson;
    private static string CommandFile(int append) => append == NativeChecksumProfileProtocol.OneAppend
        ? NativeChecksumProfileProtocol.FollowUpCommandFile : NativeChecksumProfileProtocol.SecondCommandFile;
    private static string ReceiptFile(int append) => append == NativeChecksumProfileProtocol.OneAppend
        ? NativeChecksumProfileProtocol.FollowUpReceiptFile : NativeChecksumProfileProtocol.SecondReceiptFile;
}
