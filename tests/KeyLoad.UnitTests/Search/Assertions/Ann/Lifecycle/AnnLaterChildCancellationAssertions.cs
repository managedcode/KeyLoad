using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnLaterChildCancellationAssertions
{
    internal static async Task RunAsync(TestDatabase database, AnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest request, QueryObservedWorkClock clock)
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
        var next = AnnMaintenanceTestData.Read(database, request, upper);
        await Assert.That(next.Consumer.Checkpoint).IsEqualTo(first.ThroughSequence);
        var original = runtime.Owner.ObserveWork(runtime.SessionId)
            ?? throw new InvalidOperationException("The original execution replay work observation is absent.");
        var before = AnnMaintenanceTestData.Snapshot(database);
        var files = AnnFixtureFileSnapshot.Capture(database.Directory);
        var position = database.Store.Position;
        using var child = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        clock.Arm(() => runtime.Owner.ObserveWork(runtime.SessionId) is { } actual
            && actual.SessionId == original.SessionId && actual.WorkUnits > original.WorkUnits, child.Cancel);
        try
        {
            var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => runtime.PhaseAsync(database,
                request, AnnMaintenanceCapabilityKind.ApplyPage, next, token: child.Token))
                ?? throw new InvalidOperationException("The original later child cancellation was not observed during execution replay.");
            await Assert.That(error.CancellationToken).IsEqualTo(child.Token);
        }
        finally { clock.Disarm(); }
        await Assert.That(clock.Triggered).IsTrue();
        await Assert.That(runtime.Owner.ObserveWork(runtime.SessionId)!.Value.WorkUnits).IsGreaterThan(original.WorkUnits);
        await Assert.That(AnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(AnnFixtureFileSnapshot.Capture(database.Directory)).IsEquivalentTo(files, CollectionOrdering.Matching);
        _ = await AnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent);
    }
}
