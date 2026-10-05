using System.Runtime.InteropServices;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PartitionRecordPageTests
{
    private const string Family = "document";
    private static readonly PartitionRef Partition = new(PartitionRecordNativeFixture.TenantId,
        PartitionRecordNativeFixture.DatabaseId, PartitionRecordNativeFixture.DomainId, PartitionRecordNativeFixture.PartitionKey);
    private static readonly PartitionRef OtherPartition = Partition with { PartitionKey = "partition-b" };
    private static readonly PartitionRef OtherTenant = Partition with { TenantId = "tenant-b" };
    private static readonly PartitionRef OtherDatabase = Partition with { DatabaseId = "database-b" };
    private static readonly PartitionRef OtherDomain = Partition with { TransactionDomainId = "domain-b" };

    [Test]
    public async Task AcPmove001And003PagesPreserveRawBytesOrderCutAndFullScope()
    {
        using var fixture = new PartitionRecordNativeFixture();
        var a = PartitionRecordNativeFixture.Row(Family, Partition, "a", "value-a");
        var b = PartitionRecordNativeFixture.Row(Family, Partition, "b", "value-b");
        var c = PartitionRecordNativeFixture.Row(Family, Partition, "c", "value-c");
        PartitionRecordNativeFixture.Seed(fixture.Store, c, b, a);

        var pages = fixture.Store.Read(view =>
        {
            var first = PartitionRecordPageReader.Read(view, Partition, Family, 2, 1024, 2048);
            var second = PartitionRecordPageReader.Read(view, Partition, Family, 2, 1024, 2048,
                first.Continuation!.Value);
            return (first, second);
        });
        await Assert.That(PartitionRecordNativeFixture.PageKeys(pages.first.Records)).IsEqualTo(
            string.Join('|', PartitionRecordNativeFixture.Hex(a.Key), PartitionRecordNativeFixture.Hex(b.Key)));
        await Assert.That(PartitionRecordNativeFixture.PageValues(pages.first.Records)).IsEqualTo(
            string.Join('|', PartitionRecordNativeFixture.Hex(a.Value), PartitionRecordNativeFixture.Hex(b.Value)));
        await Assert.That(pages.first.HasMore).IsTrue();
        await Assert.That(pages.first.Continuation.HasValue).IsTrue();
        var continuation = pages.first.Continuation!.Value;
        await Assert.That(PartitionRecordNativeFixture.Hex(continuation.Span))
            .IsEqualTo(PartitionRecordNativeFixture.Hex(b.Key));
        AssertContinuationOwnsIndependentBuffer(pages.first.Records[^1].Key, continuation);
        await Assert.That(PartitionRecordNativeFixture.PageKeys(pages.second.Records))
            .IsEqualTo(PartitionRecordNativeFixture.Hex(c.Key));
        await Assert.That(PartitionRecordNativeFixture.PageValues(pages.second.Records))
            .IsEqualTo(PartitionRecordNativeFixture.Hex(c.Value));
        await Assert.That(pages.second.HasMore).IsFalse();
        await Assert.That(pages.second.Continuation.HasValue).IsFalse();
        var firstRecordBytes = a.Key.Length + a.Value.Length + b.Key.Length + b.Value.Length;
        var continuationBytes = b.Key.Length;
        var secondRecordBytes = c.Key.Length + c.Value.Length;
        await Assert.That(pages.first.RetainedBytes).IsEqualTo(firstRecordBytes + continuationBytes);
        await Assert.That(pages.first.RetainedBytes + pages.second.RetainedBytes)
            .IsEqualTo(firstRecordBytes + continuationBytes + secondRecordBytes);
        await Assert.That(pages.second.RetainedBytes).IsEqualTo(c.Key.Length + c.Value.Length);

    }

    [Test]
    public async Task AcPmove001FullPartitionAndFamilyComponentsIsolateEqualSuffixKeys()
    {
        using var fixture = new PartitionRecordNativeFixture();
        var rows = new[]
        {
            PartitionRecordNativeFixture.Row(Family, Partition, "same", "selected"),
            PartitionRecordNativeFixture.Row(Family, OtherPartition, "same", "partition"),
            PartitionRecordNativeFixture.Row(Family, OtherTenant, "same", "tenant"),
            PartitionRecordNativeFixture.Row(Family, OtherDatabase, "same", "database"),
            PartitionRecordNativeFixture.Row(Family, OtherDomain, "same", "domain"),
            PartitionRecordNativeFixture.Row("vector", Partition, "same", "family")
        };
        PartitionRecordNativeFixture.Seed(fixture.Store, rows);
        await AssertValuesAsync(fixture.Store, Partition, Family, "selected");
        await AssertValuesAsync(fixture.Store, OtherPartition, Family, "partition");
        await AssertValuesAsync(fixture.Store, OtherTenant, Family, "tenant");
        await AssertValuesAsync(fixture.Store, OtherDatabase, Family, "database");
        await AssertValuesAsync(fixture.Store, OtherDomain, Family, "domain");
        await AssertValuesAsync(fixture.Store, Partition, "vector", "family");
    }

    [Test]
    public async Task AcPmove003ReopenedNativeStoreReproducesExactPageBytes()
    {
        using var fixture = new PartitionRecordNativeFixture();
        var rows = new[]
        {
            PartitionRecordNativeFixture.Row(Family, Partition, "a", "one\0raw"),
            PartitionRecordNativeFixture.Row(Family, Partition, "b", "two\u00ffraw")
        };
        PartitionRecordNativeFixture.Seed(fixture.Store, rows);
        var before = PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 2, 1024, 1024);
        fixture.Reopen();
        var after = PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 2, 1024, 1024);
        await Assert.That(PartitionRecordNativeFixture.PageKeys(after.Records))
            .IsEqualTo(PartitionRecordNativeFixture.PageKeys(before.Records));
        await Assert.That(PartitionRecordNativeFixture.PageValues(after.Records))
            .IsEqualTo(PartitionRecordNativeFixture.PageValues(before.Records));
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
        var page = PartitionRecordNativeFixture.Read(store, partition, family, 2, 1024, 2048);
        await Assert.That(page.Records.Length).IsEqualTo(1);
        await Assert.That(PartitionRecordNativeFixture.PageValues(page.Records))
            .IsEqualTo(PartitionRecordNativeFixture.Hex(System.Text.Encoding.UTF8.GetBytes(expectedValue)));
    }
}
