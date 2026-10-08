using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Verifies complete retired source model deletion, exact original A receipt replay and released blob accounting.</summary>
internal static class ControlledPartitionMovementRetiredSourceAssertions
{
    private const int Version = 1;
    private const int EmptyCount = 0;

    internal static async Task AssertAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementOutcomeAuthority originalAuthority,
        byte[] originalReceipt, DateTimeOffset originalRecordedAt, CancellationToken cancellationToken)
    {
        await Assert.That(ControlledPartitionMovementTargetModelImage.Read(source).Length).IsEqualTo(EmptyCount);
        var expected = new BlobQuota(Version, source.Store.Identity.Incarnation,
            EmptyCount, EmptyCount, EmptyCount, EmptyCount);
        var global = source.Store.Read(view => BlobRecordReader.Get<BlobQuota>(view, BlobKeys.Global));
        var quota = source.Store.Read(view => BlobRecordReader.Get<BlobQuota>(view,
            BlobKeys.Quota(ControlledPartitionMovementBlobSeed.Blob)));
        await Assert.That(JsonDefaults.Serialize(global).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(quota).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await originalAuthority.RemainsControlOwnedAsync(source, target);
        var before = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var position = source.Store.Position;
        var index = source.Journal.Log.State.LastIndex;
        var replay = source.Journal.Submit(ControlledPartitionMovementCorpus.SeedOperation(source.Database,
            originalRecordedAt), cancellationToken);
        await ControlledPartitionMovementReceiptAssertions.ReplayAsync(replay, originalReceipt);
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(index);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store)
            .SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
    }
}
