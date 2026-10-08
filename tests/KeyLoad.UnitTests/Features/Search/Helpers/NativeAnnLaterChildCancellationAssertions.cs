using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeAnnLaterChildCancellationAssertions
{
    internal static async Task RunAsync(TestDatabase database, NativeAnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest request, QueryObservedWorkClock clock)
    {
        using var beganToken = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        var began = await runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.Begin, token: beganToken.Token);
        var upper = began.Source!.ThroughSequence;
        var first = NativeAnnMaintenanceTestData.Read(database, request, upper);
        var intent = NativeAnnMaintenanceTestData.Intent(first);
        _ = await runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.ApplyPage, first, token: beganToken.Token);
        _ = await runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.StagePage, intent: intent, token: beganToken.Token);
        _ = await NativeAnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent);
        beganToken.Dispose();
        var next = NativeAnnMaintenanceTestData.Read(database, request, upper);
        await Assert.That(next.Consumer.Checkpoint).IsEqualTo(first.ThroughSequence);
        var original = runtime.Owner.ObserveWork(runtime.SessionId)
            ?? throw new InvalidOperationException("The original native replay work observation is absent.");
        var before = NativeAnnMaintenanceTestData.Snapshot(database);
        var files = NativeAnnFixtureFileSnapshot.Capture(database.Directory);
        var position = database.Store.Position;
        using var child = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        clock.Arm(() => runtime.Owner.ObserveWork(runtime.SessionId) is { } actual
            && actual.SessionId == original.SessionId && actual.WorkUnits > original.WorkUnits, child.Cancel);
        try
        {
            var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => runtime.PhaseAsync(database,
                request, AnnMaintenanceCapabilityKind.ApplyPage, next, token: child.Token))
                ?? throw new InvalidOperationException("The original later child cancellation was not observed during native replay.");
            await Assert.That(error.CancellationToken).IsEqualTo(child.Token);
        }
        finally { clock.Disarm(); }
        await Assert.That(clock.Triggered).IsTrue();
        await Assert.That(runtime.Owner.ObserveWork(runtime.SessionId)!.Value.WorkUnits).IsGreaterThan(original.WorkUnits);
        await Assert.That(NativeAnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(NativeAnnFixtureFileSnapshot.Capture(database.Directory)).IsEquivalentTo(files, CollectionOrdering.Matching);
        _ = await NativeAnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent);
    }
}
