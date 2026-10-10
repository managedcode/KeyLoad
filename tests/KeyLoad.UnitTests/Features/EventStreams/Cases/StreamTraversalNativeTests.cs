namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class StreamTraversalNativeTests
{
    [Test]
    public async Task ForwardAndNativeReverseRetainFullLiteralPagesAcrossSameRootReopen()
    {
        using var fixture = new StreamReadResourceFixture(eventCount: StreamTraversalTestProtocol.Third, protectedFields: true);
        var before = fixture.Cut();
        var forward = fixture.Traverse(fixture.Traversal(limit: StreamTraversalTestProtocol.Second));
        var reverse = fixture.Traverse(fixture.Traversal(StreamReadDirection.Backward, limit: StreamTraversalTestProtocol.Second));
        await StreamTraversalNativeAssertions.PageAsync(forward, [StreamTraversalTestProtocol.First, StreamTraversalTestProtocol.Second], StreamTraversalTestProtocol.Third, true);
        await StreamTraversalNativeAssertions.PageAsync(reverse, [StreamTraversalTestProtocol.Third, StreamTraversalTestProtocol.Second], StreamTraversalTestProtocol.Third, true);
        fixture.Reopen(new());
        var lastForward = fixture.Traverse(fixture.Traversal(limit: StreamTraversalTestProtocol.Second, cursor: forward.Cursor));
        var lastReverse = fixture.Traverse(fixture.Traversal(StreamReadDirection.Backward, limit: StreamTraversalTestProtocol.Second, cursor: reverse.Cursor));
        await StreamTraversalNativeAssertions.PageAsync(lastForward, [StreamTraversalTestProtocol.Third], StreamTraversalTestProtocol.Third, false);
        await StreamTraversalNativeAssertions.PageAsync(lastReverse, [StreamTraversalTestProtocol.First], StreamTraversalTestProtocol.Third, false);
        await Assert.That(lastForward.SnapshotCutPosition).IsEqualTo(forward.SnapshotCutPosition);
        await Assert.That(lastReverse.SnapshotCutPosition).IsEqualTo(reverse.SnapshotCutPosition);
        await StreamTraversalNativeAssertions.UnchangedAsync(fixture, before);
    }

    [Test]
    public async Task ByteRefusalTamperedScopeAndCancellationLeaveCompleteNativeCutThenHealthyTraversal()
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
            fixture.Traversal(cursor: valid.Cursor + "x")));
        await Assert.That(tampered.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Traverse(fixture.Traversal(), cancelled.Token));
        await StreamTraversalNativeAssertions.UnchangedAsync(fixture, before);
        var healthy = fixture.Traverse(fixture.Traversal(limit: StreamTraversalTestProtocol.First, maxBytes: bytes, cursor: valid.Cursor));
        await StreamTraversalNativeAssertions.PageAsync(healthy, [StreamTraversalTestProtocol.Second], StreamTraversalTestProtocol.Third, true);
        await StreamTraversalNativeAssertions.UnchangedAsync(fixture, before);
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
        await StreamTraversalNativeAssertions.UnchangedAsync(fixture, before);
        var healthy = fixture.Traverse(fixture.Traversal(StreamReadDirection.Backward));
        await Assert.That(healthy.Head).IsEqualTo(new StreamHead(StreamTraversalTestProtocol.Third, StreamTraversalTestProtocol.Second, StreamTraversalTestProtocol.First));
        await Assert.That(healthy.Events.Select(record => record.Revision).SequenceEqual([StreamTraversalTestProtocol.Third, StreamTraversalTestProtocol.Second])).IsTrue();
        await Assert.That(healthy.HasMore).IsFalse();
        await StreamTraversalNativeAssertions.UnchangedAsync(fixture, before);
    }
    [Test]
    public async Task PersistedAuthorizationPrecedesMalformedCursorAndChangedEpochRequiresFreshHealthyTraversal()
    {
        using var fixture = new StreamReadResourceFixture(eventCount: StreamTraversalTestProtocol.Third, protectedFields: true);
        var original = fixture.Traverse(fixture.Traversal(limit: StreamTraversalTestProtocol.First));
        fixture.SetReader(false);
        var refusedCut = fixture.Cut();
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Traverse(fixture.Traversal(cursor: original.Cursor + "x")));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await StreamTraversalNativeAssertions.UnchangedAsync(fixture, refusedCut);
        fixture.SetReader(true);
        var restoredCut = fixture.Cut();
        var stale = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Traverse(fixture.Traversal(cursor: original.Cursor)));
        await Assert.That(stale.Code).IsEqualTo(ErrorCode.CursorExpired);
        await StreamTraversalNativeAssertions.UnchangedAsync(fixture, restoredCut);
        await StreamTraversalNativeAssertions.PageAsync(fixture.Traverse(fixture.Traversal()), [StreamTraversalTestProtocol.First, StreamTraversalTestProtocol.Second, StreamTraversalTestProtocol.Third], StreamTraversalTestProtocol.Third, false);
        await StreamTraversalNativeAssertions.UnchangedAsync(fixture, restoredCut);
    }

    [Test]
    public async Task ActualNativeLogicalBindingInvalidatesOriginalDirectoryFenceThenFreshColdTraversalIsHealthy()
    {
        using var fixture = new StreamReadResourceFixture(eventCount: StreamTraversalTestProtocol.Third, protectedFields: true);
        var original = fixture.Traverse(fixture.Traversal(limit: StreamTraversalTestProtocol.First));
        fixture.BindOriginalPlacement();
        var boundCut = fixture.Cut();
        var stale = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Traverse(fixture.Traversal(cursor: original.Cursor)));
        await Assert.That(stale.Code).IsEqualTo(ErrorCode.CursorExpired);
        await StreamTraversalNativeAssertions.UnchangedAsync(fixture, boundCut);
        fixture.Reopen(new());
        await StreamTraversalNativeAssertions.PageAsync(fixture.Traverse(fixture.Traversal()),
            [StreamTraversalTestProtocol.First, StreamTraversalTestProtocol.Second, StreamTraversalTestProtocol.Third],
            StreamTraversalTestProtocol.Third, false);
        await StreamTraversalNativeAssertions.UnchangedAsync(fixture, boundCut);
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
        await StreamTraversalNativeAssertions.UnchangedAsync(fixture, before);
        await StreamTraversalNativeAssertions.PageAsync(fixture.Traverse(fixture.Traversal()),
            [StreamTraversalTestProtocol.First, StreamTraversalTestProtocol.Second, StreamTraversalTestProtocol.Third],
            StreamTraversalTestProtocol.Third, false);
        await StreamTraversalNativeAssertions.UnchangedAsync(fixture, before);
    }

    [Test]
    public async Task ReorderedBorrowedConfiguredVotersRefuseWithoutEffectsThenExactNativeOwnerIsHealthy()
    {
        using var fixture = new StreamReadResourceFixture(eventCount: StreamTraversalTestProtocol.Third, protectedFields: true);
        var actual = fixture.Placement;
        var owner = new PhysicalShardRecord(actual.PhysicalShardId, actual.Incarnation, actual.VoterIds, actual.PlacementEpoch);
        var before = fixture.Cut();
        var foreign = owner with { VoterIds = [.. owner.VoterIds.Reverse()] };
        var refused = Assert.ThrowsExactly<KeyLoadException>(() => fixture.TraverseConfigured(fixture.Traversal(), foreign));
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await StreamTraversalNativeAssertions.UnchangedAsync(fixture, before);
        await StreamTraversalNativeAssertions.PageAsync(fixture.TraverseConfigured(fixture.Traversal(), owner),
            [StreamTraversalTestProtocol.First, StreamTraversalTestProtocol.Second, StreamTraversalTestProtocol.Third],
            StreamTraversalTestProtocol.Third, false);
        await StreamTraversalNativeAssertions.UnchangedAsync(fixture, before);
    }

}
