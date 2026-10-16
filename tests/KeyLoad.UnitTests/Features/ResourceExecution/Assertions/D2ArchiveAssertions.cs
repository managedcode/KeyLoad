using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ArchiveAssertions
{
    private const string PreviousIncarnation = "This command belongs to a previous database incarnation.";

    internal static async Task SourceAsync(D2ArchiveSource source, CancellationToken token)
    {
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(source.Owner);
        await D2ResourceInventoryAssertions.EqualAsync(source.Outcome, source.Owner.Journal.Submit(source.Original, token));
        await D2ResourceInventoryAssertions.EqualAsync(source.Outcome, source.Owner.Journal.Submit(source.NativeOriginal, token));
        await RetainedAsync(source.Owner.Store, source.Partition, source.Original.Id, source.RetainedOutcome);
        await DataAsync(source.Owner.Database, source, false);
        await D2ArchiveQuery.VerifyAsync(source.Owner.Database, source.Owner.Store, source, source.Registered, token);
        await before.AssertUnchangedAsync(source.Owner);
    }

    internal static async Task DataAsync(DatabaseEngine database, D2ArchiveSource source, bool healthy)
    {
        await DocumentAsync(database, source.Partition, source.DocumentsLifetime.ResourceName, D2ArchiveData.SeedId, D2ArchiveData.SeedJson);
        await DocumentAsync(database, source.Partition, source.UnrelatedLifetime.ResourceName, D2ArchiveData.UnrelatedId,
            D2ArchiveData.UnrelatedJson);
        if (healthy)
        {
            await DocumentAsync(database, source.Partition, source.DocumentsLifetime.ResourceName,
                D2ArchiveData.HealthyId, D2ArchiveData.HealthyJson);
        }
        else
        {
            await Assert.That(database.GetDocument(D2ArchiveData.Root,
                new(source.Partition, source.DocumentsLifetime.ResourceName, D2ArchiveData.HealthyId))).IsNull();
        }
        await Assert.That(database.Authenticate(D2ArchiveData.Credential, database.EvaluationClock.GetUtcNow())).IsEqualTo(D2ArchiveData.Root);
    }

    internal static async Task<OperationResult> GenerationAsync(DatabaseEngine database, IAtomicStore store,
        D2ArchiveSource source)
    {
        var position = store.Position;
        var image = ControlledPartitionMovementRawImage.Bytes(store);
        var publicRefusal = database.Apply(source.Original);
        await D2ResourceInventoryAssertions.EqualAsync(new OperationResult(null, ErrorCode.TokenInvalidated, PreviousIncarnation),
            publicRefusal);
        var nativeRefusal = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(source.NativeOriginal));
        await Assert.That(nativeRefusal.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(nativeRefusal.Message).IsEqualTo(NativeCommandContract.InvalidSignature);
        await RetainedAsync(store, source.Partition, source.Original.Id, source.RetainedOutcome);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        return publicRefusal;
    }

    internal static async Task RetainedAsync(IAtomicStore store, PartitionRef partition, Guid id, byte[] expected)
    {
        var actual = store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(partition, D2ArchiveData.Root, id)));
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.AsSpan().SequenceEqual(expected)).IsTrue();
    }

    private static async Task DocumentAsync(DatabaseEngine database, PartitionRef partition, string collection, string id, string json)
    {
        var expected = new DocumentResult(new(partition, collection, id), D2ArchiveData.Single, json, false, []);
        await D2ResourceInventoryAssertions.EqualAsync(expected, database.GetDocument(D2ArchiveData.Root, expected.Reference));
    }
}
