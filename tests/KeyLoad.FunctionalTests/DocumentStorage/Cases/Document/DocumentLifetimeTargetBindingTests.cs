using KeyLoad.Core.DocumentStorage;
using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Storage;
using A = KeyLoad.FunctionalTests.DocumentStorage.DocumentSessionAuthorityColdAssertions;

namespace KeyLoad.FunctionalTests.DocumentStorage;

internal sealed class DocumentLifetimeTargetBindingTests
{
    private const string OtherCollection = "other-lifetime-documents";
    private const int ColdReopens = 2;

    [Test]
    public Task OtherResourceCreationCannotReplaceStoredBindingThenExactRepairReplaysAcrossCold()
        => DocumentPatchAtomicityOwner.RunAsync(async fixture =>
        {
            fixture.Source.Configure(A.Collection, ResourceKind.Collection);
            fixture.Source.Configure(OtherCollection, ResourceKind.Collection);
            var receipt = fixture.Source.Commit(new PutDocument(A.Collection, A.Id, A.Json, 0),
                new PutDocument(OtherCollection, A.Id, A.Json, 0));
            var issued = fixture.Entry(receipt.CommandId).PreparedCommand;
            var other = fixture.Database.GetDocument(A.Root, new(fixture.Source.Partition, OtherCollection, A.Id))!
                .Reference.Lifetime;
            var key = DocumentStorageKeys.RecordKey(A.Reference(fixture));
            var original = fixture.Store.Read(view => view.ReadOwnedValue(key))
                ?? throw new InvalidOperationException(A.Collection);
            var record = OrleansSerialization.Deserialize<DocumentRecord>(original);
            await Assert.That(record.Reference.Lifetime).IsNotEqualTo(other);
            fixture.Store.Commit((tx, _) =>
            {
                tx.PutRecord(key, record with { Reference = record.Reference with { Lifetime = other } });
                return true;
            });
            try
            {
                var image = QueueWholeFlowStorage.Bytes(fixture.Store);
                var position = fixture.Store.Position;
                var failure = Assert.ThrowsExactly<KeyLoadException>(() => A.Read(fixture, receipt.Token));
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
                await A.UnchangedAsync(fixture, image, position);
            }
            finally
            {
                fixture.Store.Commit((tx, _) => { tx.Put(key, original); return true; });
            }
            for (var cold = 0; cold <= ColdReopens; cold++)
            {
                await A.LiteralAsync(fixture, receipt.Token, 1);
                await A.ReplayAsync(fixture, issued, receipt);
                if (cold < ColdReopens)
                {
                    await fixture.ReopenAsync();
                }
            }
        });
}
