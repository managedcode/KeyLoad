namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PartitionRecordPageBoundsTests
{
    private const string Family = "document";
    private static readonly PartitionRef Partition = new(PartitionRecordNativeFixture.TenantId,
        PartitionRecordNativeFixture.DatabaseId, PartitionRecordNativeFixture.DomainId, PartitionRecordNativeFixture.PartitionKey);

    [Test]
    public async Task AcPmove002EmptyExactAndLookaheadPagesChargeIndependentLimits()
    {
        using var fixture = new PartitionRecordNativeFixture();
        var empty = PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 1, 1, 1);
        await Assert.That(empty.Records.IsEmpty).IsTrue();
        await Assert.That(empty.HasMore).IsFalse();
        await Assert.That(empty.RetainedBytes).IsEqualTo(0);
        await Assert.That(empty.ExaminedBytes).IsEqualTo(0);

        var rows = new[]
        {
            PartitionRecordNativeFixture.Row(Family, Partition, "a", "1"),
            PartitionRecordNativeFixture.Row(Family, Partition, "b", "22"),
            PartitionRecordNativeFixture.Row(Family, Partition, "c", "333")
        };
        PartitionRecordNativeFixture.Seed(fixture.Store, rows);
        var retainedRecords = rows[0].Key.Length + rows[0].Value.Length
            + rows[1].Key.Length + rows[1].Value.Length;
        var continuationBytes = rows[1].Key.Length;
        var retained = retainedRecords + continuationBytes;
        var examined = rows.Sum(row => (long)row.Key.Length + row.Value.Length);
        var exact = PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 2, retained, examined);
        await Assert.That(exact.Records.Length).IsEqualTo(2);
        await Assert.That(exact.HasMore).IsTrue();
        await Assert.That(exact.RetainedBytes).IsEqualTo(retained);
        await Assert.That(exact.ExaminedBytes).IsEqualTo(examined);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 2, retained - 1, examined)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 2, retainedRecords, examined)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 2, retained, examined - 1)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        var healthy = PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 2, retained, examined);
        await Assert.That(healthy.Records.Length).IsEqualTo(2);
        await Assert.That(healthy.RetainedBytes).IsEqualTo(retained);
        await Assert.That(healthy.RetainedBytes).IsEqualTo(retainedRecords + continuationBytes);
    }

    [Test]
    public async Task AcPmove002OverlargeRecordFailsBeforeReturningPartialPage()
    {
        using var fixture = new PartitionRecordNativeFixture();
        var row = PartitionRecordNativeFixture.Row(Family, Partition, "large", new string('x', 4096));
        PartitionRecordNativeFixture.Seed(fixture.Store, row);
        var recordBytes = row.Key.Length + row.Value.Length;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 2, recordBytes - 1, recordBytes));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var healthy = PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family, 1, recordBytes, recordBytes);
        await Assert.That(healthy.Records.Length).IsEqualTo(1);
        await Assert.That(healthy.RetainedBytes).IsEqualTo(recordBytes);
    }
}
