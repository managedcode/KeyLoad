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
            baselinePage.ExaminedBytes, rows.Length,
            MaximumPageBytes, MaximumPageBytes);

        await Assert.That(outcome.ObserverFailure).IsNull();
        await Assert.That(outcome.ExaminedBytesBeyondBaseline).IsGreaterThan(baselinePage.ExaminedBytes);
        await Assert.That(outcome.Page).IsNull();
        var operationCanceled = outcome.ReadFailure as OperationCanceledException;
        await Assert.That(operationCanceled).IsNotNull();
        await Assert.That(operationCanceled!.CancellationToken).IsEqualTo(outcome.CancellationToken);
        await Assert.That(outcome.CancellationToken.IsCancellationRequested).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);

        await Assert.That(fixture.Store.GetReadDiagnostics().RangeExaminedBytes - baseline.RangeExaminedBytes)
            .IsEqualTo(outcome.ExaminedBytesBeyondBaseline);
        await PartitionRecordCancellationStateAssertions.VerifyHealthyAsync(fixture.Store, Partition, Family,
            rows, position, MaximumPageBytes);
    }

    [Test]
    public async Task AcPmove003ScopedObserverFailurePreservesOriginalExceptionStateAndHealthyPage()
    {
        using var fixture = new PartitionRecordNativeFixture();
        var rows = new[] { PartitionRecordNativeFixture.Row(Family, Partition, "a", "first"),
            PartitionRecordNativeFixture.Row(Family, Partition, "b", "second") };
        PartitionRecordNativeFixture.Seed(fixture.Store, rows);
        var position = fixture.Store.Position;
        var sentinel = new InvalidOperationException("Caller examined-work admission rejected the native read.");
        KeyLoad.Core.Features.ClusterRouting.Contracts.PartitionRecordPage? page = null;
        Exception? failure = null;
        var observations = 0;
        long observedBytes = 0;
        try
        {
            page = fixture.Store.Read(view => KeyLoad.Core.Features.ClusterRouting.Queries.PartitionRecordPageReader.Read(
                view, Partition, Family, rows.Length, MaximumPageBytes, MaximumPageBytes, bytes =>
                {
                    observations++;
                    observedBytes = bytes;
                    throw sentinel;
                }));
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }
        await Assert.That(ReferenceEquals(failure, sentinel)).IsTrue();
        await Assert.That(page).IsNull();
        await Assert.That(observations).IsEqualTo(1);
        await Assert.That(observedBytes).IsEqualTo((long)rows[0].Key.Length + rows[0].Value.Length);
        await PartitionRecordCancellationStateAssertions.VerifyHealthyAsync(fixture.Store, Partition, Family,
            rows, position, MaximumPageBytes);
    }

    [Test]
    public async Task AcPmove002ScopedObservationChargesEachNativeRecordAndLookaheadExactlyOnce()
    {
        using var fixture = new PartitionRecordNativeFixture();
        var rows = new[] { PartitionRecordNativeFixture.Row(Family, Partition, "a", "first"),
            PartitionRecordNativeFixture.Row(Family, Partition, "b", "second") };
        PartitionRecordNativeFixture.Seed(fixture.Store, rows);
        var position = fixture.Store.Position;
        var charges = new List<long>();
        var page = fixture.Store.Read(view => KeyLoad.Core.Features.ClusterRouting.Queries.PartitionRecordPageReader.Read(
            view, Partition, Family, BaselinePageRecords, MaximumPageBytes, MaximumPageBytes, charges.Add));
        await Assert.That(charges.SequenceEqual(rows.Select(row => (long)row.Key.Length + row.Value.Length))).IsTrue();
        await Assert.That(page.ExaminedBytes).IsEqualTo(charges.Sum());
        await Assert.That(page.Records.Length).IsEqualTo(BaselinePageRecords);
        await Assert.That(page.HasMore).IsTrue();
        await Assert.That(page.Continuation.HasValue).IsTrue();
        await Assert.That(page.Continuation!.Value.Span.SequenceEqual(rows[0].Key)).IsTrue();
        await Assert.That(page.RetainedBytes).IsEqualTo((long)rows[0].Key.Length * 2 + rows[0].Value.Length);
        await PartitionRecordCancellationStateAssertions.VerifyHealthyAsync(fixture.Store, Partition, Family,
            rows, position, MaximumPageBytes);
    }
}
