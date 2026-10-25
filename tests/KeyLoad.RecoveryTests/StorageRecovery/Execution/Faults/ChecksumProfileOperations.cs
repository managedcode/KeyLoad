using KeyLoad.Core;
using KeyLoad.RecoveryTests.DocumentStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests;

internal static class ChecksumProfileOperations
{
    internal static async Task RequireOriginalAsync(string root, DatabaseEngine database, ZoneTreeStore store,
        int appends, bool replay)
    {
        var operation = await CommandIdempotencyCrashData.ReadEvidenceAsync<Command<DatabaseCommand>>(root, CommandIdempotencyCrashContract.CommandEvidenceFile);
        var receipt = await CommandIdempotencyCrashData.ReadEvidenceAsync<CommitReceipt>(root, CommandIdempotencyCrashContract.ReceiptEvidenceFile);
        var tail = await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(root, CommandIdempotencyCrashContract.SeedTailEvidenceFile);
        CommandIdempotencyCrashAssertions.RequireSameReceipt(database.ResolveOutcome(operation).Get<CommitReceipt>(), receipt);
        CommandIdempotencyCrashAssertions.AssertCanonicalEffects(database, store, receipt,
            tail + ChecksumProfileProtocol.OriginalEffects + appends, tail);
        if (replay)
        {
            CommandIdempotencyCrashAssertions.AssertRetriesAndConflict(database, store, operation, receipt,
                tail + ChecksumProfileProtocol.OriginalEffects + appends, tail);
        }
    }

    internal static async Task AppendAsync(string root, DatabaseEngine database, ZoneTreeStore store, int append)
    {
        var operation = Create(database, append);
        var receipt = database.Apply(operation).Get<CommitReceipt>();
        RequireReceipt(receipt, append);
        CommandIdempotencyCrashAssertions.RequireSameReceipt(database.Apply(operation).Get<CommitReceipt>(), receipt);
        if (Errors.Code(database.Apply(CommandIdempotencyCrashData.CreateChangedOperation(database, operation)).Problem).Value != ErrorCode.Conflict)
        {
            throw new InvalidOperationException(ChecksumProfileProtocol.Invalid);
        }
        await CommandIdempotencyCrashData.SaveEvidenceAsync(root, CommandFile(append), operation);
        await CommandIdempotencyCrashData.SaveEvidenceAsync(root, ReceiptFile(append), receipt);
        await RequireAppendAsync(root, database, store, append);
    }

    internal static async Task RequireAppendAsync(string root, DatabaseEngine database, ZoneTreeStore store, int append)
    {
        var operation = await CommandIdempotencyCrashData.ReadEvidenceAsync<Command<DatabaseCommand>>(root, CommandFile(append));
        var receipt = await CommandIdempotencyCrashData.ReadEvidenceAsync<CommitReceipt>(root, ReceiptFile(append));
        CommandIdempotencyCrashAssertions.RequireSameReceipt(database.ResolveOutcome(operation).Get<CommitReceipt>(), receipt);
        RequireReceipt(receipt, append);
        var retained = store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(CommandIdempotencyCrashContract.Partition,
            CrashFixtureValues.Principal, CommandId(append))))?.Result.Get<CommitReceipt>()
            ?? throw new InvalidOperationException(ChecksumProfileProtocol.Invalid);
        CommandIdempotencyCrashAssertions.RequireSameReceipt(retained, receipt);
        var reference = CommandIdempotencyCrashData.ExpectedDocumentReference(database, DocumentId(append));
        CommandIdempotencyCrashAssertions.RequireDocument(database.GetDocument(CrashFixtureValues.Principal, reference),
            reference, ChecksumProfileProtocol.InitialRevision, Json(append));
        var seedTail = await CommandIdempotencyCrashData.ReadEvidenceAsync<long>(root, CommandIdempotencyCrashContract.SeedTailEvidenceFile);
        RequireOutbox(store, receipt, seedTail + ChecksumProfileProtocol.OriginalEffects + append, append);
    }

    private static void RequireOutbox(ZoneTreeStore store, CommitReceipt receipt, long sequence, int append)
    {
        var entry = store.Read(view => view.GetRecord<OutboxEntry>(KeySpace.Partition(CommandIdempotencyCrashContract.TailKeySpace,
            CommandIdempotencyCrashContract.Partition, sequence)));
        if (entry is null || entry.Commit != receipt.Token || entry.Sequence != sequence
            || entry.Ordinal != ChecksumProfileProtocol.EmptyRevision
            || !CommandIdempotencyCrashAssertions.SameMutationReceipt(entry.Receipt, receipt.Mutations[ChecksumProfileProtocol.EmptyRevision])
            || entry.Mutation is not PutDocument document || document.Json != Json(append)
            || document.Collection != CommandIdempotencyCrashContract.Collection || document.Id != DocumentId(append)
            || document.ExpectedRevision != ChecksumProfileProtocol.EmptyRevision)
        {
            throw new InvalidOperationException(ChecksumProfileProtocol.Invalid);
        }
    }

    private static void RequireReceipt(CommitReceipt receipt, int append)
    {
        if (receipt.CommandId != CommandId(append) || receipt.Mutations.Length != ChecksumProfileProtocol.OneAppend)
        {
            throw new InvalidOperationException(ChecksumProfileProtocol.Invalid);
        }
        var mutation = receipt.Mutations[ChecksumProfileProtocol.EmptyRevision];
        if (mutation.Kind != ChecksumProfileProtocol.PutDocumentKind || mutation.Resource != CommandIdempotencyCrashContract.Collection
            || mutation.Id != DocumentId(append) || mutation.Revision != ChecksumProfileProtocol.InitialRevision)
        {
            throw new InvalidOperationException(ChecksumProfileProtocol.Invalid);
        }
    }

    private static Command<DatabaseCommand> Create(DatabaseEngine database, int append)
    {
        var id = CommandId(append);
        var request = new CommandRequest(id, CommandIdempotencyCrashContract.Partition,
            [new PutDocument(CommandIdempotencyCrashContract.Collection, DocumentId(append), Json(append), ChecksumProfileProtocol.EmptyRevision)]);
        return CrashDatabase.Operation(database, OperationKind.Batch, request, id);
    }

    private static Guid CommandId(int append) => append == ChecksumProfileProtocol.OneAppend
        ? CommandIdempotencyCrashContract.FollowUpCommandId : Guid.Parse(ChecksumProfileProtocol.SecondCommandText);
    private static string DocumentId(int append) => append == ChecksumProfileProtocol.OneAppend
        ? CommandIdempotencyCrashContract.FollowUpDocumentId : ChecksumProfileProtocol.SecondDocumentId;
    private static string Json(int append) => append == ChecksumProfileProtocol.OneAppend
        ? CommandIdempotencyCrashContract.FollowUpJson : ChecksumProfileProtocol.SecondJson;
    private static string CommandFile(int append) => append == ChecksumProfileProtocol.OneAppend
        ? ChecksumProfileProtocol.FollowUpCommandFile : ChecksumProfileProtocol.SecondCommandFile;
    private static string ReceiptFile(int append) => append == ChecksumProfileProtocol.OneAppend
        ? ChecksumProfileProtocol.FollowUpReceiptFile : ChecksumProfileProtocol.SecondReceiptFile;
}
