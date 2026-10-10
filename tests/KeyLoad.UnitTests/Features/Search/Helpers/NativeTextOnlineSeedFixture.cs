using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed record NativeTextOnlineSeedFixture(NativeTextSeedPin Pin, DocumentRecord[] Expected)
{
    internal static async Task<NativeTextOnlineSeedFixture> CreateAsync(TestDatabase database,
        CancellationToken cancellationToken)
    {
        database.Configure(NativeTextBilingualAudit.Collection, ResourceKind.Collection);
        var now = database.Database.EvaluationClock.GetUtcNow();
        var id = Guid.NewGuid();
        var access = new RowAccess(NativeTextMaintenanceTestValues.Principal);
        var command = new CommandRequest(id, database.Partition,
            [new PutDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.UkrainianId,
                NativeTextBilingualAudit.UkrainianJson, Access: access),
             new PutDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.EnglishId,
                NativeTextBilingualAudit.EnglishJson, Access: access)]);
        var receipt = database.Submit(OperationKind.Batch, command, id: id, time: now).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(command.Mutations.Length);
        var request = await NativeTextMaintenanceRequestFixture.CreateAsync(database, cancellationToken);
        DocumentRecord[] expected = [
            new(new(database.Partition, request.Collection, NativeTextBilingualAudit.EnglishId), 1,
                NativeTextBilingualAudit.EnglishJson, access, now),
            new(new(database.Partition, request.Collection, NativeTextBilingualAudit.UkrainianId), 1,
                NativeTextBilingualAudit.UkrainianJson, access, now)];
        return new(new(request.Consumer, request.IndexGeneration, request.Collection,
            request.Field, request.NodeId, request.Placement), expected);
    }

    internal long FirstRetainedAndBothNativeRowsBytes()
    {
        var first = NativeSerialization.Serialize(Expected[0]).LongLength;
        var second = NativeSerialization.Serialize(Expected[1]).LongLength;
        return checked(DocumentStorageKeys.RecordKey(Expected[0].Reference).LongLength + first
            + ZoneTreePersistenceFormat.StorageValueHeaderBytes + first + IntPtr.Size
            + DocumentStorageKeys.RecordKey(Expected[1].Reference).LongLength + second
            + ZoneTreePersistenceFormat.StorageValueHeaderBytes);
    }
}
