using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;
using C = KeyLoad.UnitTests.Features.DocumentStorage.RemoteDocumentNativeFixture;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentLifetimeTargetBindingTests
{
    private const string MissingRecord = "The original native destination document is absent.";
    private const int ColdReopens = 2;

    [Test]
    public Task ValidSourceCreationCannotReplaceTargetStoredBindingThenExactRepairReplaysAcrossTwoColdOwners()
        => RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            fixture.Seed();
            fixture.GrantDestination();
            await Assert.That(fixture.SourceCreation.Incarnation).IsNotEqualTo(fixture.DestinationCreation.Incarnation);
            var key = DocumentStorageKeys.RecordKey(C.Reference);
            var original = fixture.Destination.Store.Read(view => view.ReadOwnedValue(key))
                ?? throw new InvalidOperationException(MissingRecord);
            var record = NativeSerialization.Deserialize<DocumentRecord>(original);
            await Assert.That(record.Reference).IsEqualTo(fixture.ExpectedDestinationReference);
            await Assert.That(record.Revision).IsEqualTo(C.First);
            await Assert.That(record.Json).IsEqualTo(C.OriginalJson);
            var transplanted = record with
            {
                Reference = C.Reference with
                {
                    Lifetime = new(fixture.SourceCreation.Incarnation.Value)
                }
            };
            fixture.Destination.Store.Commit((transaction, _) =>
            {
                transaction.PutRecord(key, transplanted);
                return true;
            });
            try
            {
                await RefuseUnchangedAsync(fixture);
            }
            finally
            {
                fixture.Destination.Store.Commit((transaction, _) =>
                {
                    transaction.Put(key, original);
                    return true;
                });
            }
            var restored = fixture.Destination.Store.Read(view => view.ReadOwnedValue(key))
                ?? throw new InvalidOperationException(MissingRecord);
            await Assert.That(restored.AsSpan().SequenceEqual(original)).IsTrue();
            await HealthyAcrossColdAsync(fixture);
        });

    private static async Task RefuseUnchangedAsync(RemoteDocumentNativeFixture fixture)
    {
        var frame = DocumentLifetimeNativeRead.Capture(fixture);
        var source = RemoteDocumentNativeAssertions.Image(fixture.Source);
        var target = RemoteDocumentNativeAssertions.Image(fixture.Destination);
        var sourcePosition = fixture.Source.Store.Position;
        var targetPosition = fixture.Destination.Store.Position;
        OwnedDocumentReadResultV1? published = null;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            published = DocumentLifetimeNativeRead.Read(fixture, frame));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(published).IsNull();
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(fixture.Destination.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Source).SequenceEqual(source)).IsTrue();
        await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(target)).IsTrue();
    }

    private static async Task HealthyAcrossColdAsync(RemoteDocumentNativeFixture fixture)
    {
        for (var cold = default(int); cold <= ColdReopens; cold++)
        {
            fixture.RequireOriginalResourceBindings();
            var frame = DocumentLifetimeNativeRead.Capture(fixture);
            var position = fixture.Destination.Store.Position;
            await RemoteDocumentNativeAssertions.DocumentAsync(DocumentLifetimeNativeRead.Read(fixture, frame), fixture, position);
            fixture.Source.Database.ValidateRemoteDocumentReadV2(frame, DocumentLifetimeNativeRead.Work(fixture.Source.Database));
            await RemoteDocumentNativeAssertions.ReceiptAsync(fixture);
            var replay = fixture.Destination.Database.Apply(fixture.Write).Get<CommitReceipt>();
            await Assert.That(NativeSerialization.Serialize(replay).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(fixture.Receipt))).IsTrue();
            if (cold < ColdReopens)
            {
                var source = RemoteDocumentNativeAssertions.Image(fixture.Source);
                var target = RemoteDocumentNativeAssertions.Image(fixture.Destination);
                fixture.Source.Reopen();
                fixture.Destination.Reopen();
                await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Source).SequenceEqual(source)).IsTrue();
                await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(target)).IsTrue();
            }
        }
    }
}
