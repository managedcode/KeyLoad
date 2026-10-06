namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeReadCutCancellationTests
{
    private const string RecordId = "one";
    private const string RowValue = "value";

    [Test]
    public async Task AcCut002CancellationBeforeAndDuringTraversalIsJoinedAndReusable()
    {
        using var fixture = new NativeReadCutFixture();
        fixture.Store.Commit((tx, _) => { tx.Put(NativeReadCutFixture.Key(RecordId), NativeReadCutFixture.Value(RowValue)); return true; });
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var captureCancelled = Assert.ThrowsExactly<OperationCanceledException>(() =>
            fixture.Capture(NativeReadCutFixture.Limits(4, 128), source.Token));
        await Assert.That(captureCancelled.CancellationToken.IsCancellationRequested).IsTrue();

        using var traversalSource = new CancellationTokenSource();
        using var before = fixture.Capture(NativeReadCutFixture.Limits(4, 128), traversalSource.Token);
        await traversalSource.CancelAsync();
        var cancelled = Assert.ThrowsExactly<OperationCanceledException>(() => before.VisitPrefix(
            NativeReadCutFixture.Key(string.Empty), static (_, _) => true));
        await Assert.That(cancelled.CancellationToken.IsCancellationRequested).IsTrue();
        before.Dispose();

        using var duringSource = new CancellationTokenSource();
        using var during = fixture.Capture(NativeReadCutFixture.Limits(4, 128), duringSource.Token);
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => during.VisitPrefix(
            NativeReadCutFixture.Key(string.Empty), (_, _) =>
            {
                duringSource.Cancel();
                return true;
            }));
        await Assert.That(failure.CancellationToken.IsCancellationRequested).IsTrue();
        during.Dispose();

        using var healthy = fixture.Capture(NativeReadCutFixture.Limits(4, 128));
        var result = healthy.VisitPrefix(NativeReadCutFixture.Key(string.Empty), static (_, _) => true);
        await Assert.That(result.Records).IsEqualTo(1);
    }

    [Test]
    public async Task AcCut002IdleLeaseExpiryFailsBeforeAnotherNativeAdvanceAndStillReleasesSlot()
    {
        using var fixture = new NativeReadCutFixture();
        fixture.Store.Commit((tx, _) => { tx.Put(NativeReadCutFixture.Key(RecordId), NativeReadCutFixture.Value(RowValue)); return true; });
        var lease = fixture.Capture(NativeReadCutFixture.Limits(4, 128, TimeSpan.FromSeconds(1)));
        await Task.Delay(TimeSpan.FromMilliseconds(1_100), TimeProvider.System);
        var expired = Assert.ThrowsExactly<KeyLoadException>(() => lease.VisitPrefix(
            NativeReadCutFixture.Key(string.Empty), static (_, _) => true));
        await Assert.That(expired.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(lease.Cut.Position).IsEqualTo(fixture.Store.Position);
        var disposal = Assert.ThrowsExactly<KeyLoadException>(lease.Dispose);
        await Assert.That(disposal.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        using var healthy = fixture.Capture(NativeReadCutFixture.Limits(4, 128));
        await Assert.That(healthy.Cut.Position).IsEqualTo(fixture.Store.Position);
    }
}
