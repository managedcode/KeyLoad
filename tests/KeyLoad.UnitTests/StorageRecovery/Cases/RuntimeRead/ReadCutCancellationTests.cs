namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed class ReadCutCancellationTests
{
    private const string RecordId = "one";
    private const string RowValue = "value";

    [Test]
    public async Task AcCut002CancellationBeforeAndDuringTraversalIsJoinedAndReusable()
    {
        using var fixture = new ReadCutFixture();
        fixture.Store.Commit((tx, _) => { tx.Put(ReadCutFixture.Key(RecordId), ReadCutFixture.Value(RowValue)); return true; });
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var captureCancelled = Assert.ThrowsExactly<OperationCanceledException>(() =>
            fixture.Capture(ReadCutFixture.Limits(4, 128), source.Token));
        await Assert.That(captureCancelled.CancellationToken.IsCancellationRequested).IsTrue();

        using var traversalSource = new CancellationTokenSource();
        using var before = fixture.Capture(ReadCutFixture.Limits(4, 128), traversalSource.Token);
        await traversalSource.CancelAsync();
        var cancelled = Assert.ThrowsExactly<OperationCanceledException>(() => before.VisitPrefix(
            ReadCutFixture.Key(string.Empty), static (_, _) => true));
        await Assert.That(cancelled.CancellationToken.IsCancellationRequested).IsTrue();
        before.Dispose();

        using var duringSource = new CancellationTokenSource();
        using var during = fixture.Capture(ReadCutFixture.Limits(4, 128), duringSource.Token);
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => during.VisitPrefix(
            ReadCutFixture.Key(string.Empty), (_, _) =>
            {
                duringSource.Cancel();
                return true;
            }));
        await Assert.That(failure.CancellationToken.IsCancellationRequested).IsTrue();
        during.Dispose();

        using var healthy = fixture.Capture(ReadCutFixture.Limits(4, 128));
        var result = healthy.VisitPrefix(ReadCutFixture.Key(string.Empty), static (_, _) => true);
        await Assert.That(result.Records).IsEqualTo(1);
    }

    [Test]
    public async Task AcCut002IdleLeaseExpiryFailsBeforeAnotherAdvanceAndStillReleasesSlot()
    {
        var clock = new ReadCutAdvancingClock();
        using var fixture = new ReadCutFixture(clock);
        fixture.Store.Commit((tx, _) => { tx.Put(ReadCutFixture.Key(RecordId), ReadCutFixture.Value(RowValue)); return true; });
        var lease = fixture.Capture(ReadCutFixture.Limits(4, 128, TimeSpan.FromSeconds(1)));
        clock.Advance(TimeSpan.FromMilliseconds(1_100));
        var expired = Assert.ThrowsExactly<KeyLoadException>(() => lease.VisitPrefix(
            ReadCutFixture.Key(string.Empty), static (_, _) => true));
        await Assert.That(expired.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(lease.Cut.Position).IsEqualTo(fixture.Store.Position);
        var disposal = Assert.ThrowsExactly<KeyLoadException>(lease.Dispose);
        await Assert.That(disposal.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        using var healthy = fixture.Capture(ReadCutFixture.Limits(4, 128));
        await Assert.That(healthy.Cut.Position).IsEqualTo(fixture.Store.Position);
    }
}
