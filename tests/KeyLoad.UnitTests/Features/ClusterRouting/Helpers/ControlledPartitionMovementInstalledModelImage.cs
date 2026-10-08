using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Checks all original transferred native model rows and explicit destination blob ownership/quota.</summary>
internal static class ControlledPartitionMovementInstalledModelImage
{
    private const int Version = 1;
    private const long BlobBytes = 4;
    private const int OneBlob = 1;
    private const int NoUploads = 0;

    internal static async Task AssertAsync(ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovementPageResult[] originalPages)
    {
        var pages = originalPages.Select(result => NativeSerialization.Deserialize<PartitionMoveImagePage>(
            result.NativePage.Span)).ToArray();
        foreach (var page in pages)
        { await AssertPageAsync(target, corpus, page); }
        var expectedQuota = new BlobQuota(Version, corpus.Destination.Owner.Incarnation,
            BlobBytes, OneBlob, OneBlob, NoUploads);
        var quota = target.Store.Read(view => BlobRecordReader.Get<BlobQuota>(view,
            BlobKeys.Quota(ControlledPartitionMovementBlobSeed.Blob)));
        var global = target.Store.Read(view => BlobRecordReader.Get<BlobQuota>(view, BlobKeys.Global));
        await Assert.That(JsonDefaults.Serialize(quota).SequenceEqual(JsonDefaults.Serialize(expectedQuota))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(global).SequenceEqual(JsonDefaults.Serialize(expectedQuota))).IsTrue();
        var actualRows = ControlledPartitionMovementTargetModelImage.Read(target);
        await Assert.That(actualRows.Length).IsEqualTo(pages.Sum(page => page.Records.Length));
    }

    private static async Task AssertPageAsync(ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMoveImagePage page)
    {
        foreach (var record in page.Records)
        {
            var actual = target.Store.Read(view => view.ReadOwnedValue(record.Key.ToArray()))
                ?? throw new InvalidOperationException("An original transferred native model row is absent.");
            if (page.Family == PartitionRecordFamilies.BlobHead)
            {
                var original = NativeSerialization.Deserialize<BlobHead>(record.Value.Span);
                var expected = original with { Incarnation = corpus.Destination.Owner.Incarnation };
                await Assert.That(JsonDefaults.Serialize(NativeSerialization.Deserialize<BlobHead>(actual))
                    .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            }
            else if (page.Family == PartitionRecordFamilies.BlobState)
            {
                var original = NativeSerialization.Deserialize<BlobState>(record.Value.Span);
                await Assert.That(original.IntegrityIncarnation).IsEqualTo(corpus.Control.Owner.Incarnation);
                var expected = original with { Incarnation = corpus.Destination.Owner.Incarnation };
                await Assert.That(JsonDefaults.Serialize(NativeSerialization.Deserialize<BlobState>(actual))
                    .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            }
            else
            { await Assert.That(actual.AsSpan().SequenceEqual(record.Value.Span)).IsTrue(); }
        }
    }
}
