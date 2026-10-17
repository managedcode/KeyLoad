using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PartitionMoveCleanupReadBudgetTrial
{
    private const string PartitionKey = "partition-a";
    private const int FirstOrdinal = 0;
    private const int SecondOrdinal = 1;
    private const int SingleRecord = 1;
    private const int RawImageCeiling = 4;
    private const int FirstBytes = 16;
    private const int SecondBytes = 32;
    private const long OneUnder = 1;
    private static readonly PartitionRef Partition = new("tenant-a", "database-a", "domain-a", PartitionKey);
    private static readonly BlobRef Blob = new(Partition, "files", "cleanup-original");
    private static readonly Guid Upload = Guid.Parse("a5ae4fd1-15b2-49a6-b6a4-cf6e1239001d");

    internal static async Task RunAsync(PartitionRecordNativeFixture fixture)
    {
        var first = (Key: BlobKeys.Part(Blob, Upload, FirstOrdinal), Value: Enumerable.Range(FirstOrdinal, FirstBytes).Select(value => (byte)value).ToArray());
        var second = (Key: BlobKeys.Part(Blob, Upload, SecondOrdinal), Value: Enumerable.Range(FirstOrdinal, SecondBytes).Select(value => (byte)(value + FirstBytes)).ToArray());
        PartitionRecordNativeFixture.Seed(fixture.Store, first, second);
        var retained = checked((long)first.Key.Length + first.Value.Length);
        var examined = checked(retained + second.Key.Length + second.Value.Length);
        var image = Capture(fixture);
        var position = fixture.Store.Position;
        await RefuseAsync(fixture, retained, examined - OneUnder, image, position);
        await RefuseAsync(fixture, retained - OneUnder, examined, image, position);
        await HealthyAsync(fixture, first, retained, examined);
        fixture.Reopen();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(Capture(fixture)).IsEqualTo(image);
        await RefuseAsync(fixture, retained, examined - OneUnder, image, position);
        await HealthyAsync(fixture, first, retained, examined);
        fixture.Reopen();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(Capture(fixture)).IsEqualTo(image);
        await HealthyAsync(fixture, first, retained, examined);
    }

    private static ScanPage Read(PartitionRecordNativeFixture fixture, long retained, long examined)
        => fixture.Store.Read(view => PartitionMoveCleanupPageReader.Read(view,
            KeySpace.Partition(BlobKeys.PartSpace, Partition), SingleRecord, retained, examined));

    private static async Task RefuseAsync(PartitionRecordNativeFixture fixture, long retained, long examined,
        string image, long position)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() => Read(fixture, retained, examined));
        await Assert.That(error!.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(error.Message).IsEqualTo("The partition movement exceeds its admitted image capacity.");
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(Capture(fixture)).IsEqualTo(image);
    }

    private static async Task HealthyAsync(PartitionRecordNativeFixture fixture,
        (byte[] Key, byte[] Value) first, long retained, long examined)
    {
        var page = Read(fixture, retained, examined);
        await Assert.That(page.Records.Length).IsEqualTo(SingleRecord);
        await Assert.That(page.HasMore).IsTrue();
        await Assert.That(page.Records.Single().Key.Span.SequenceEqual(first.Key)).IsTrue();
        await Assert.That(page.Records.Single().Value.Span.SequenceEqual(first.Value)).IsTrue();
    }

    private static string Capture(PartitionRecordNativeFixture fixture)
        => fixture.Store.Read(view => string.Join('|', view.Scan([], RawImageCeiling).Records.Select(row =>
            Convert.ToHexString(row.Key.Span) + ':' + Convert.ToHexString(row.Value.Span))));
}
