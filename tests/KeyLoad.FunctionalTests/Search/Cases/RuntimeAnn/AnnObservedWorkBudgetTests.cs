using KeyLoad.Clustering;
using KeyLoad.Server;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnObservedWorkBudgetTests
{
    private const int Count = 3;

    [Test]
    public async Task ActualOwnedVerifyWorkHonorsOriginalCancellationThenPublishesHealthy()
    {
        using var database = AnnSeedTestSupport.Create(Count, new DatabaseLimits());
        var request = AnnMaintenanceTestData.Pin(database);
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new AnnMaintenanceTestRuntime(database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                _ = await VerifyFailureAndHealthyAsync(database, actual, request);
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task<AnnMaintenanceCapabilityResult> VerifyFailureAndHealthyAsync(TestDatabase database, AnnMaintenanceTestRuntime actual,
        AnnMaintenanceRequest request)
    {
        var began = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Begin);
        var built = await AnnMaintenancePhaseAssertions.FinishAsync(database, actual, request, began.Source!.ThroughSequence);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        var before = AnnMaintenanceTestData.Snapshot(database);
        var position = database.Store.Position;
        var work = database.Store.GetReadDiagnostics();
        await using var observed = new ObservedWorkCancellation(database.Store, original,
            static progress => progress.RangeExaminedBytes);
        await observed.Ready;
        AnnMaintenanceCapabilityResult? partial = null;
        await AssertFailureAsync(async () => partial = await actual.PhaseAsync(database, request,
            AnnMaintenanceCapabilityKind.Verify, token: original.Token), original.Token);
        await Assert.That(observed.CancelledAfterProgress).IsTrue();
        await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsGreaterThan(work.RangeExaminedBytes);
        await Assert.That(partial).IsNull();
        await Assert.That(AnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        _ = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Verify);
        var healthy = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Publish);
        await Assert.That(healthy.Count).IsEqualTo(Count);
        await Assert.That(healthy.IndexSha256).IsEqualTo(built.IndexSha256);
        await Assert.That(healthy.Source!.CorpusSha256).IsEqualTo(built.Source!.CorpusSha256);
        return built;
    }

    private static async Task AssertFailureAsync(Func<Task> operation, CancellationToken token)
    {
        var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(operation)
            ?? throw new InvalidOperationException("The original execution ANN cancellation was not observed.");
        await Assert.That(error.CancellationToken).IsEqualTo(token);
    }
}
