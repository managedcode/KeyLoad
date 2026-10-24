using KeyLoad.Core.DocumentStorage;
using KeyLoad.Core.Search;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Search;

internal sealed record TextOnlineSeedFixture(TextSeedPin Pin, DocumentRecord[] Expected)
{
    internal static async Task<TextOnlineSeedFixture> CreateAsync(TestDatabase database,
        CancellationToken cancellationToken)
    {
        database.Configure(TextBilingualAudit.Collection, ResourceKind.Collection);
        var now = database.Database.EvaluationClock.GetUtcNow();
        var id = Guid.NewGuid();
        var access = new RowAccess(TextMaintenanceTestValues.Principal);
        var command = new CommandRequest(id, database.Partition,
            [new PutDocument(TextBilingualAudit.Collection, TextBilingualAudit.UkrainianId,
                TextBilingualAudit.UkrainianJson, Access: access),
             new PutDocument(TextBilingualAudit.Collection, TextBilingualAudit.EnglishId,
                TextBilingualAudit.EnglishJson, Access: access)]);
        var receipt = database.Submit(OperationKind.Batch, command, id: id, time: now).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(command.Mutations.Length);
        var request = await TextMaintenanceRequestFixture.CreateAsync(database, cancellationToken);
        DocumentRecord[] expected = [
            new(database.ExpectedReference(new(database.Partition, request.Collection, TextBilingualAudit.EnglishId)), 1,
                TextBilingualAudit.EnglishJson, access, now),
            new(database.ExpectedReference(new(database.Partition, request.Collection, TextBilingualAudit.UkrainianId)), 1,
                TextBilingualAudit.UkrainianJson, access, now)];
        return new(new(request.Consumer, request.IndexGeneration, request.Collection,
            request.Field, request.NodeId, request.Placement), expected);
    }

    internal long FirstRetainedAndBothRowsBytes()
    {
        var first = OrleansSerialization.Serialize(Expected[0]).LongLength;
        var second = OrleansSerialization.Serialize(Expected[1]).LongLength;
        return checked(DocumentStorageKeys.RecordKey(Expected[0].Reference).LongLength + first
            + ZoneTreePersistenceFormat.StorageValueHeaderBytes + first + IntPtr.Size
            + DocumentStorageKeys.RecordKey(Expected[1].Reference).LongLength + second
            + ZoneTreePersistenceFormat.StorageValueHeaderBytes);
    }
}
