namespace KeyLoad.FunctionalTests.EventStreams;

internal sealed class StreamTraversalTests
{
    [Test]
    public async Task ForwardAndReverseRetainFullLiteralPagesAcrossSameRootReopen()
    {
        using var fixture = new StreamReadResourceFixture(eventCount: StreamTraversalTestProtocol.Third, protectedFields: true);
        var before = fixture.Cut();
        var forward = fixture.Traverse(fixture.Traversal(limit: StreamTraversalTestProtocol.Second));
        var reverse = fixture.Traverse(fixture.Traversal(StreamReadDirection.Backward, limit: StreamTraversalTestProtocol.Second));
        await StreamTraversalAssertions.PageAsync(forward, [StreamTraversalTestProtocol.First, StreamTraversalTestProtocol.Second], StreamTraversalTestProtocol.Third, true);
        await StreamTraversalAssertions.PageAsync(reverse, [StreamTraversalTestProtocol.Third, StreamTraversalTestProtocol.Second], StreamTraversalTestProtocol.Third, true);
        fixture.Reopen(new());
        var lastForward = fixture.Traverse(fixture.Traversal(limit: StreamTraversalTestProtocol.Second, cursor: forward.Cursor));
        var lastReverse = fixture.Traverse(fixture.Traversal(StreamReadDirection.Backward, limit: StreamTraversalTestProtocol.Second, cursor: reverse.Cursor));
        await StreamTraversalAssertions.PageAsync(lastForward, [StreamTraversalTestProtocol.Third], StreamTraversalTestProtocol.Third, false);
        await StreamTraversalAssertions.PageAsync(lastReverse, [StreamTraversalTestProtocol.First], StreamTraversalTestProtocol.Third, false);
        await Assert.That(lastForward.SnapshotCutPosition).IsEqualTo(forward.SnapshotCutPosition);
        await Assert.That(lastReverse.SnapshotCutPosition).IsEqualTo(reverse.SnapshotCutPosition);
        await StreamTraversalAssertions.UnchangedAsync(fixture, before);
    }

    [Test]
    public async Task ByteRefusalTamperedScopeAndCancellationLeaveCompleteCutThenHealthyTraversal()
    {
        using var fixture = new StreamReadResourceFixture(eventCount: StreamTraversalTestProtocol.Third, protectedFields: true);
        var before = fixture.Cut();
        var valid = fixture.Traverse(fixture.Traversal(limit: StreamTraversalTestProtocol.First));
        var continued = fixture.Traverse(fixture.Traversal(limit: StreamTraversalTestProtocol.First, cursor: valid.Cursor));
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(continued, JsonDefaults.Options).Length;
        var tooLarge = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Traverse(fixture.Traversal(limit: StreamTraversalTestProtocol.First, maxBytes: bytes - StreamTraversalTestProtocol.First, cursor: valid.Cursor)));
        await Assert.That(tooLarge.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var wrongDirection = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Traverse(
            fixture.Traversal(StreamReadDirection.Backward, cursor: valid.Cursor)));
        await Assert.That(wrongDirection.Code).IsEqualTo(ErrorCode.CursorExpired);
        var tampered = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Traverse(
            fixture.Traversal(cursor: valid.Cursor + StreamTraversalTestProtocol.TamperedSuffix)));
        await Assert.That(tampered.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Traverse(fixture.Traversal(), cancelled.Token));
        await StreamTraversalAssertions.UnchangedAsync(fixture, before);
        var healthy = fixture.Traverse(fixture.Traversal(limit: StreamTraversalTestProtocol.First, maxBytes: bytes, cursor: valid.Cursor));
        await StreamTraversalAssertions.PageAsync(healthy, [StreamTraversalTestProtocol.Second], StreamTraversalTestProtocol.Third, true);
        await StreamTraversalAssertions.UnchangedAsync(fixture, before);
    }

    [Test]
    public async Task RetainedOriginalTraversalRejectsActualErasedFloorThenFreshBackwardPageIsHealthy()
    {
        using var fixture = new StreamReadResourceFixture(eventCount: StreamTraversalTestProtocol.Third, protectedFields: true);
        var original = fixture.Traverse(fixture.Traversal(limit: StreamTraversalTestProtocol.First));
        fixture.RetainFromRevision(StreamTraversalTestProtocol.Second);
        var before = fixture.Cut();
        var erased = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Traverse(fixture.Traversal(cursor: original.Cursor)));
        await Assert.That(erased.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await StreamTraversalAssertions.UnchangedAsync(fixture, before);
        var healthy = fixture.Traverse(fixture.Traversal(StreamReadDirection.Backward));
        await Assert.That(healthy.Head).IsEqualTo(new StreamHead(StreamTraversalTestProtocol.Third, StreamTraversalTestProtocol.Second, StreamTraversalTestProtocol.First));
        await Assert.That(healthy.Events.Select(record => record.Revision).SequenceEqual([StreamTraversalTestProtocol.Third, StreamTraversalTestProtocol.Second])).IsTrue();
        await Assert.That(healthy.HasMore).IsFalse();
        await StreamTraversalAssertions.UnchangedAsync(fixture, before);
    }
    [Test]
    public async Task PersistedAuthorizationPrecedesMalformedCursorAndChangedEpochRequiresFreshHealthyTraversal()
    {
        using var fixture = new StreamReadResourceFixture(eventCount: StreamTraversalTestProtocol.Third, protectedFields: true);
        var original = fixture.Traverse(fixture.Traversal(limit: StreamTraversalTestProtocol.First));
        fixture.SetReader(false);
        var refusedCut = fixture.Cut();
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Traverse(fixture.Traversal(cursor: original.Cursor + StreamTraversalTestProtocol.TamperedSuffix)));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await StreamTraversalAssertions.UnchangedAsync(fixture, refusedCut);
        fixture.SetReader(true);
        var restoredCut = fixture.Cut();
        var stale = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Traverse(fixture.Traversal(cursor: original.Cursor)));
        await Assert.That(stale.Code).IsEqualTo(ErrorCode.CursorExpired);
        await StreamTraversalAssertions.UnchangedAsync(fixture, restoredCut);
        await StreamTraversalAssertions.PageAsync(fixture.Traverse(fixture.Traversal()), [StreamTraversalTestProtocol.First, StreamTraversalTestProtocol.Second, StreamTraversalTestProtocol.Third], StreamTraversalTestProtocol.Third, false);
        await StreamTraversalAssertions.UnchangedAsync(fixture, restoredCut);
    }

    [Test]
    public async Task SameOriginalConfiguredExpiryRefusesCursorWithoutEffectsThenFreshCompleteTraversalIsHealthy()
    {
        var clock = new StreamTraversalObservedClock(TimeProvider.System);
        using var fixture = new StreamReadResourceFixture(eventCount: StreamTraversalTestProtocol.Third,
            protectedFields: true, timeProvider: clock);
        var original = fixture.Traverse(fixture.Traversal(limit: StreamTraversalTestProtocol.First));
        var before = fixture.Cut();
        clock.AdvanceBy(fixture.CursorLifetime);
        var expired = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Traverse(fixture.Traversal(cursor: original.Cursor)));
        await Assert.That(expired.Code).IsEqualTo(ErrorCode.CursorExpired);
        await StreamTraversalAssertions.UnchangedAsync(fixture, before);
        await StreamTraversalAssertions.PageAsync(fixture.Traverse(fixture.Traversal()),
            [StreamTraversalTestProtocol.First, StreamTraversalTestProtocol.Second, StreamTraversalTestProtocol.Third],
            StreamTraversalTestProtocol.Third, false);
        await StreamTraversalAssertions.UnchangedAsync(fixture, before);
    }

}
