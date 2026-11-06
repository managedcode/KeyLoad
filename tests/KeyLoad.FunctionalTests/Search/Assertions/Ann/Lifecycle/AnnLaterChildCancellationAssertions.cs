using KeyLoad.Clustering;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal static class AnnLaterChildCancellationAssertions
{
    internal static async Task RunAsync(TestDatabase database, AnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest request)
    {
        using var beganToken = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        var began = await runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.Begin, token: beganToken.Token);
        var upper = began.Source!.ThroughSequence;
        var first = AnnMaintenanceTestData.Read(database, request, upper);
        var intent = AnnMaintenanceTestData.Intent(first);
        _ = await runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.ApplyPage, first, token: beganToken.Token);
        _ = await runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.StagePage, intent: intent, token: beganToken.Token);
        _ = await AnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent);
        beganToken.Dispose();
        var next = database.Database.ReadProjectionBatch(AnnProjectionPinTestSupport.Principal,
            new(request.Consumer, AnnLaterChildCancellationTests.LaterPageLimit, upper));
        await Assert.That(next.Entries.Length).IsEqualTo(AnnLaterChildCancellationTests.LaterPageLimit);
        await Assert.That(next.Consumer.Checkpoint).IsEqualTo(first.ThroughSequence);
        var original = runtime.Owner.ObserveWork(runtime.SessionId)
            ?? throw new InvalidOperationException("The original execution replay work observation is absent.");
        var before = AnnMaintenanceTestData.Snapshot(database);
        var files = AnnFixtureFileSnapshot.Capture(database.Directory);
        var position = database.Store.Position;
        using var child = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        await using var observed = new ObservedWorkCancellation(
            () => runtime.Owner.ObserveWork(runtime.SessionId)?.WorkUnits ?? 0, child);
        await observed.Ready;
        var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => runtime.PhaseAsync(database,
            request, AnnMaintenanceCapabilityKind.ApplyPage, next, token: child.Token))
            ?? throw new InvalidOperationException("The original later child cancellation was not observed during execution replay.");
        await Assert.That(error.CancellationToken).IsEqualTo(child.Token);
        await Assert.That(observed.CancelledAfterProgress).IsTrue();
        await Assert.That(runtime.Owner.ObserveWork(runtime.SessionId)!.Value.WorkUnits).IsGreaterThan(original.WorkUnits);
        await Assert.That(AnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(AnnFixtureFileSnapshot.Capture(database.Directory)).IsEquivalentTo(files, CollectionOrdering.Matching);
        _ = await AnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent);
    }
}
