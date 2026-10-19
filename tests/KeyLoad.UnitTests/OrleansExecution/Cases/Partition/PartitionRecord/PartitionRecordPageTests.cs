using System.Runtime.InteropServices;
using KeyLoad.Core.Features.StoragePartitions.Queries;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class PartitionRecordPageTests
{
    private const string Family = "document";
    private static readonly PartitionRef Partition = new(PartitionRecordFixture.TenantId,
        PartitionRecordFixture.DatabaseId, PartitionRecordFixture.DomainId, PartitionRecordFixture.PartitionKey);
    private static readonly PartitionRef OtherPartition = Partition with { PartitionKey = "partition-b" };
    private static readonly PartitionRef OtherTenant = Partition with { TenantId = "tenant-b" };
    private static readonly PartitionRef OtherDatabase = Partition with { DatabaseId = "database-b" };
    private static readonly PartitionRef OtherDomain = Partition with { TransactionDomainId = "domain-b" };

    [Test]
    public async Task AcPmove001And003PagesPreserveRawBytesOrderCutAndFullScope()
    {
        using var fixture = new PartitionRecordFixture();
        var a = PartitionRecordFixture.Row(Family, Partition, "a", "value-a");
        var b = PartitionRecordFixture.Row(Family, Partition, "b", "value-b");
        var c = PartitionRecordFixture.Row(Family, Partition, "c", "value-c");
        PartitionRecordFixture.Seed(fixture.Store, c, b, a);

        var pages = fixture.Store.Read(view =>
        {
            var first = PartitionRecordPageReader.Read(view, Partition, Family, 2, 1024, 2048);
            var second = PartitionRecordPageReader.Read(view, Partition, Family, 2, 1024, 2048,
                first.Continuation!.Value);
            return (first, second);
        });
        await Assert.That(PartitionRecordFixture.PageKeys(pages.first.Records)).IsEqualTo(
            string.Join('|', PartitionRecordFixture.Hex(a.Key), PartitionRecordFixture.Hex(b.Key)));
        await Assert.That(PartitionRecordFixture.PageValues(pages.first.Records)).IsEqualTo(
            string.Join('|', PartitionRecordFixture.Hex(a.Value), PartitionRecordFixture.Hex(b.Value)));
        await Assert.That(pages.first.HasMore).IsTrue();
        await Assert.That(pages.first.Continuation.HasValue).IsTrue();
        var continuation = pages.first.Continuation!.Value;
        await Assert.That(PartitionRecordFixture.Hex(continuation.Span))
            .IsEqualTo(PartitionRecordFixture.Hex(b.Key));
        AssertContinuationOwnsIndependentBuffer(pages.first.Records[^1].Key, continuation);
        await Assert.That(PartitionRecordFixture.PageKeys(pages.second.Records))
            .IsEqualTo(PartitionRecordFixture.Hex(c.Key));
        await Assert.That(PartitionRecordFixture.PageValues(pages.second.Records))
            .IsEqualTo(PartitionRecordFixture.Hex(c.Value));
        await Assert.That(pages.second.HasMore).IsFalse();
        await Assert.That(pages.second.Continuation.HasValue).IsFalse();
        var firstRecordBytes = a.Key.Length + a.Value.Length + b.Key.Length + b.Value.Length;
        var continuationBytes = b.Key.Length;
        var secondRecordBytes = c.Key.Length + c.Value.Length;
        await Assert.That(pages.first.RetainedBytes).IsEqualTo(firstRecordBytes + continuationBytes);
        await Assert.That(pages.first.RetainedBytes + pages.second.RetainedBytes)
            .IsEqualTo(firstRecordBytes + continuationBytes + secondRecordBytes);
        await Assert.That(pages.second.RetainedBytes).IsEqualTo(c.Key.Length + c.Value.Length);
        await PartitionRecordOwnedCutContinuation.RunAsync(fixture, Partition, Family, [a, b, c],
            TestContext.Current!.Execution.CancellationToken);

    }

    [Test]
    public async Task AcPmove001FullPartitionAndFamilyComponentsIsolateEqualSuffixKeys()
    {
        using var fixture = new PartitionRecordFixture();
        var rows = new[]
        {
            PartitionRecordFixture.Row(Family, Partition, "same", "selected"),
            PartitionRecordFixture.Row(Family, OtherPartition, "same", "partition"),
            PartitionRecordFixture.Row(Family, OtherTenant, "same", "tenant"),
            PartitionRecordFixture.Row(Family, OtherDatabase, "same", "database"),
            PartitionRecordFixture.Row(Family, OtherDomain, "same", "domain"),
            PartitionRecordFixture.Row("vector", Partition, "same", "family")
        };
        PartitionRecordFixture.Seed(fixture.Store, rows);
        await AssertValuesAsync(fixture.Store, Partition, Family, "selected");
        await AssertValuesAsync(fixture.Store, OtherPartition, Family, "partition");
        await AssertValuesAsync(fixture.Store, OtherTenant, Family, "tenant");
        await AssertValuesAsync(fixture.Store, OtherDatabase, Family, "database");
        await AssertValuesAsync(fixture.Store, OtherDomain, Family, "domain");
        await AssertValuesAsync(fixture.Store, Partition, "vector", "family");
    }

    [Test]
    public async Task AcPmove003ReopenedStoreReproducesExactPageBytes()
    {
        using var fixture = new PartitionRecordFixture();
        var rows = new[]
        {
            PartitionRecordFixture.Row(Family, Partition, "a", "one\0raw"),
            PartitionRecordFixture.Row(Family, Partition, "b", "two\u00ffraw")
        };
        PartitionRecordFixture.Seed(fixture.Store, rows);
        var before = PartitionRecordFixture.Read(fixture.Store, Partition, Family, 2, 1024, 1024);
        fixture.Reopen();
        var after = PartitionRecordFixture.Read(fixture.Store, Partition, Family, 2, 1024, 1024);
        await Assert.That(PartitionRecordFixture.PageKeys(after.Records))
            .IsEqualTo(PartitionRecordFixture.PageKeys(before.Records));
        await Assert.That(PartitionRecordFixture.PageValues(after.Records))
            .IsEqualTo(PartitionRecordFixture.PageValues(before.Records));
        await Assert.That(after.ExaminedBytes).IsEqualTo(before.ExaminedBytes);
        await Assert.That(after.RetainedBytes).IsEqualTo(before.RetainedBytes);
    }

    private static void AssertContinuationOwnsIndependentBuffer(ReadOnlyMemory<byte> recordKey,
        ReadOnlyMemory<byte> continuation)
    {
        var hasRecordArray = MemoryMarshal.TryGetArray(recordKey, out var recordSegment);
        var hasContinuationArray = MemoryMarshal.TryGetArray(continuation, out var continuationSegment);
        if (!hasRecordArray || !hasContinuationArray || recordSegment.Array is null
            || continuationSegment.Array is null || ReferenceEquals(recordSegment.Array, continuationSegment.Array))
        {
            throw new InvalidOperationException("The page continuation must own a separate byte buffer.");
        }

        var original = recordSegment.Array[recordSegment.Offset];
        continuationSegment.Array[continuationSegment.Offset] ^= 0x01;
        if (recordSegment.Array[recordSegment.Offset] != original)
        {
            throw new InvalidOperationException("Mutating the continuation changed a returned record key.");
        }
    }

    private static async Task AssertValuesAsync(ZoneTreeStore store, PartitionRef partition,
        string family, string expectedValue)
    {
        var page = PartitionRecordFixture.Read(store, partition, family, 2, 1024, 2048);
        await Assert.That(page.Records.Length).IsEqualTo(1);
        await Assert.That(PartitionRecordFixture.PageValues(page.Records))
            .IsEqualTo(PartitionRecordFixture.Hex(System.Text.Encoding.UTF8.GetBytes(expectedValue)));
    }
}
