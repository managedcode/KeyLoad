using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PartitionRecordValidationTests
{
    private const string Family = "document";
    private const string UnknownFamily = "principal";
    private static readonly PartitionRef Partition = new(PartitionRecordNativeFixture.TenantId,
        PartitionRecordNativeFixture.DatabaseId, PartitionRecordNativeFixture.DomainId, PartitionRecordNativeFixture.PartitionKey);
    private static readonly PartitionRef ForeignPartition = Partition with { DatabaseId = "database-b" };
    private static readonly PartitionRef InvalidPartition = Partition with { TenantId = string.Empty };

    [Test]
    public async Task AcPmove001UnknownFamilyAndForeignContinuationFailClosed()
    {
        using var fixture = new PartitionRecordNativeFixture();
        var row = PartitionRecordNativeFixture.Row(Family, Partition, "row", "value");
        PartitionRecordNativeFixture.Seed(fixture.Store, row);
        var unknown = Assert.ThrowsExactly<KeyLoadException>(() =>
            PartitionRecordNativeFixture.Read(fixture.Store, Partition, UnknownFamily, 1, 512, 512));
        await Assert.That(unknown.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        var foreign = Assert.ThrowsExactly<KeyLoadException>(() =>
            PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 1, 512, 512,
                PartitionRecordNativeFixture.Key(Family, ForeignPartition, "row")));
        await Assert.That(foreign.Code).IsEqualTo(ErrorCode.Validation);
        var wrongFamily = Assert.ThrowsExactly<KeyLoadException>(() =>
            PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 1, 512, 512,
                PartitionRecordNativeFixture.Key("vector", Partition, "row")));
        await Assert.That(wrongFamily.Code).IsEqualTo(ErrorCode.Validation);
        var shortContinuationBudget = Assert.ThrowsExactly<KeyLoadException>(() =>
            PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 1, 512, 1, row.Key));
        await Assert.That(shortContinuationBudget.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var invalidPartition = Assert.ThrowsExactly<KeyLoadException>(() =>
            PartitionRecordNativeFixture.Read(fixture.Store, InvalidPartition, Family, 1, 512, 512));
        await Assert.That(invalidPartition.Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task AcPmove002And003InvalidBoundsAndCancellationReturnNoPage()
    {
        using var fixture = new PartitionRecordNativeFixture();
        var row = PartitionRecordNativeFixture.Row(Family, Partition, "row", "value");
        PartitionRecordNativeFixture.Seed(fixture.Store, row);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 0, 512, 512));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 1, 0, 512)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 1, 512, 0)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.That(() => PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 1, 512, 512,
            cancellationToken: canceled.Token)).Throws<OperationCanceledException>();
        var healthy = PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 1, 512, 512);
        await Assert.That(healthy.Records.Length).IsEqualTo(1);
    }

    [Test]
    public async Task AcPmove003MalformedNativeCodecContinuationFailsBeforeTraversal()
    {
        using var fixture = new PartitionRecordNativeFixture();
        var row = PartitionRecordNativeFixture.Row(Family, Partition, "row", "value");
        PartitionRecordNativeFixture.Seed(fixture.Store, row);
        var prefix = KeySpace.Partition(Family, Partition);
        var invalidSuffix = new byte[] { KeyCodec.Version, 0x50, 0xC0, 0x00, 0x00 };
        var malformed = new byte[prefix.Length + invalidSuffix.Length - 1];
        prefix.CopyTo(malformed, 0);
        invalidSuffix.AsSpan(1).CopyTo(malformed.AsSpan(prefix.Length));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => KeyCodec.Decode(malformed)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        var examinedLimit = malformed.Length + 1L;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 1, 512, examinedLimit, malformed));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        var healthy = PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 1, 512, 512);
        await Assert.That(healthy.Records.Length).IsEqualTo(1);
    }
}
