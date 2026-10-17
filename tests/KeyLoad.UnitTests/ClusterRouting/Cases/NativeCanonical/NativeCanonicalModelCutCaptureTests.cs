using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeCanonicalModelCutCaptureTests
{
    [Test]
    public async Task OriginalCancellationRefusesBeforeBorrowingNativeOwnerWithoutChanges()
    {
        await C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            var before = fixture.Store.Position;
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            var error = Assert.ThrowsExactly<OperationCanceledException>(() =>
                NativeCanonicalModelCutCapture.Capture(fixture.Database, null!, Request(fixture), cancelled.Token));
            await Assert.That(error.CancellationToken).IsEqualTo(cancelled.Token);
            await Assert.That(fixture.Store.Position).IsEqualTo(before);
        });
    }

    [Test]
    public async Task WholeNativeRecordOverEnvelopeBudgetIsRefusedWithoutTruncation()
    {
        await C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            var key = KeySpace.Partition(PartitionRecordFamilies.Document, fixture.OtherPartition, "over-envelope");
            var bytes = new byte[NativeCanonicalModelCutCodec.MaximumRecordBytes + 1];
            fixture.Store.Commit((transaction, position) => { transaction.Put(key, bytes); return position; });
            var before = fixture.Store.Position;
            var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view =>
                new NativeCanonicalModelCutReader(UnitExecutionOptions.DatabaseLimits(fixture.Database.Limits), CancellationToken.None)
                    .Read(view, Request(fixture), null!, fixture.Store.Position)));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(fixture.Store.Position).IsEqualTo(before);
            await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(key))!.SequenceEqual(bytes)).IsTrue();
        });
    }

    [Test]
    public async Task OriginalAccumulatedReadBudgetsRefuseBeforeAnyWitnessIsProduced()
    {
        await C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            var before = fixture.Store.Position;
            var records = fixture.Database.Limits with
            {
                MaxScanRecords = 1
            };
            var bytes = fixture.Database.Limits with
            {
                MaxQueryReadBytes = 1
            };
            foreach (var limits in new[] { records, bytes })
            {
                var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view =>
                    new NativeCanonicalModelCutReader(UnitExecutionOptions.DatabaseLimits(limits), CancellationToken.None)
                        .Read(view, Request(fixture), null!, fixture.Store.Position)));
                await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            }
            await Assert.That(fixture.Store.Position).IsEqualTo(before);
        });
    }

    private static NativeCanonicalModelCutRequest Request(C1OutcomeInspectionFixture fixture)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("N"), fixture.OtherPartition,
            fixture.Partition, fixture.CommandId, C1OutcomeInspectionAssertions.AdminId, "cut-technical-subject",
            new(fixture.OtherPartition, "source-queue"), Guid.NewGuid(), new(fixture.Partition, "target-queue"));
}
