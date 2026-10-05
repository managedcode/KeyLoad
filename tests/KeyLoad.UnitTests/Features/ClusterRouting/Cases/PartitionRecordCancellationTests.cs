using KeyLoad.UnitTests.Features.ClusterRouting.Helpers;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PartitionRecordCancellationTests
{
    private const string Family = "document";
    private const int BaselinePageRecords = 1;
    private const long MaximumPageBytes = 64L * 1024 * 1024;

    private static readonly PartitionRef Partition = new(PartitionRecordNativeFixture.TenantId,
        PartitionRecordNativeFixture.DatabaseId, PartitionRecordNativeFixture.DomainId,
        PartitionRecordNativeFixture.PartitionKey);

    [Test]
    public async Task AcPmove003CancellationAfterObservedNativeWorkReturnsNoPartialPage()
    {
        using var fixture = new PartitionRecordNativeFixture();
        var rows = PartitionRecordCancellationSeed.Create(Partition, Family);
        PartitionRecordNativeFixture.Seed(fixture.Store, rows);
        var baselinePage = PartitionRecordNativeFixture.Read(fixture.Store, Partition, Family,
            BaselinePageRecords, MaximumPageBytes, MaximumPageBytes);
        var baseline = fixture.Store.GetReadDiagnostics();
        var position = fixture.Store.Position;
        var outcome = PartitionRecordCancellationRunner.Run(fixture.Store, Partition, Family,
            baseline.RangeExaminedBytes, baselinePage.ExaminedBytes, rows.Length,
            MaximumPageBytes, MaximumPageBytes);

        await Assert.That(outcome.ObserverFailure).IsNull();
        await Assert.That(outcome.ExaminedBytesBeyondBaseline).IsGreaterThan(baselinePage.ExaminedBytes);
        await Assert.That(outcome.Page).IsNull();
        var operationCanceled = outcome.ReadFailure as OperationCanceledException;
        await Assert.That(operationCanceled).IsNotNull();
        await Assert.That(operationCanceled!.CancellationToken).IsEqualTo(outcome.CancellationToken);
        await Assert.That(outcome.CancellationToken.IsCancellationRequested).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);

        var storedValue = fixture.Store.Read(view => view.ReadOwnedValue(rows[^1].Key));
        await Assert.That(storedValue is not null && storedValue.AsSpan().SequenceEqual(rows[^1].Value)).IsTrue();
    }
}
