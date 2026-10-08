using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeAnnObservedWorkBudgetTests
{
    private const int Count = 3;
    private const int DeadlineSeconds = 1;
    private const int ExpiredSeconds = 2;
    private const string DeadlineDetail = "The read execution deadline is exceeded.";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualOwnedNativeVerifyWorkRejectsOriginalCancellationOrElapsedBudgetThenPublishesHealthy(bool cancel)
    {
        using var database = AnnSeedTestSupport.Create(Count, new DatabaseLimits { QueryDeadlineSeconds = DeadlineSeconds });
        var request = NativeAnnMaintenanceTestData.Pin(database);
        var clock = new QueryObservedWorkClock();
        AnnMaintenanceCapabilityResult? built = null;
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new NativeAnnMaintenanceTestRuntime(database, clock);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                built = await VerifyFailureAndHealthyAsync(database, actual, request, clock, cancel);
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        if (!cancel)
        {
            await RestoreHealthyWithFreshParentAsync(database, request with
            { CommandId = Guid.NewGuid(), Mode = AnnMaintenanceMode.Restore }, clock,
                built ?? throw new InvalidOperationException("The original native ANN generation was not retained."));
        }
    }
    private static async Task<AnnMaintenanceCapabilityResult> VerifyFailureAndHealthyAsync(TestDatabase database, NativeAnnMaintenanceTestRuntime actual,
        AnnMaintenanceRequest request, QueryObservedWorkClock clock, bool cancel)
    {
        var began = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Begin);
        var built = await NativeAnnMaintenancePhaseAssertions.FinishAsync(database, actual, request, began.Source!.ThroughSequence);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        var before = NativeAnnMaintenanceTestData.Snapshot(database);
        var position = database.Store.Position;
        var work = database.Store.GetReadDiagnostics();
        clock.Arm(() => database.Store.GetReadDiagnostics().RangeExaminedBytes > work.RangeExaminedBytes,
            () => { if (cancel) { original.Cancel(); } else { clock.Advance(TimeSpan.FromSeconds(ExpiredSeconds)); } });
        AnnMaintenanceCapabilityResult? partial = null;
        try
        {
            await AssertFailureAsync(async () => partial = await actual.PhaseAsync(database, request,
                AnnMaintenanceCapabilityKind.Verify, token: original.Token), cancel, original.Token);
        }
        finally { clock.Disarm(); }
        await Assert.That(clock.Triggered).IsTrue();
        await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsGreaterThan(work.RangeExaminedBytes);
        await Assert.That(partial).IsNull();
        await Assert.That(NativeAnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        if (!cancel)
        {
            await AssertFailureAsync(() => actual.PhaseAsync(database, request,
                AnnMaintenanceCapabilityKind.Verify), cancel: false, TestContext.Current!.Execution.CancellationToken);
            await Assert.That(NativeAnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
            await Assert.That(database.Store.Position).IsEqualTo(position);
            return built;
        }
        _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Verify);
        var healthy = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Publish);
        await Assert.That(healthy.Count).IsEqualTo(Count);
        await Assert.That(healthy.IndexSha256).IsEqualTo(built.IndexSha256);
        await Assert.That(healthy.Source!.CorpusSha256).IsEqualTo(built.Source!.CorpusSha256);
        return built;
    }

    private static async Task RestoreHealthyWithFreshParentAsync(TestDatabase database, AnnMaintenanceRequest request,
        QueryObservedWorkClock originalAdvancedClock, AnnMaintenanceCapabilityResult built)
    {
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new NativeAnnMaintenanceTestRuntime(database, originalAdvancedClock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var began = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime,
                    request, AnnMaintenanceCapabilityKind.Begin);
                if (began.OriginalCheckpointIntent is { } intent)
                { _ = await NativeAnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent); }
                _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Load);
                _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Verify);
                var healthy = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Publish);
                await Assert.That(healthy.Count).IsEqualTo(Count);
                await Assert.That(healthy.IndexSha256).IsEqualTo(built.IndexSha256);
                await Assert.That(healthy.Source!.CorpusSha256).IsEqualTo(built.Source!.CorpusSha256);
            }, failures);
        }
        catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task AssertFailureAsync(Func<Task> operation, bool cancel, CancellationToken token)
    {
        if (cancel)
        {
            var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(operation)
                ?? throw new InvalidOperationException("The original native ANN cancellation was not observed.");
            await Assert.That(error.CancellationToken).IsEqualTo(token);
        }
        else
        {
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(operation)
                ?? throw new InvalidOperationException("The native ANN elapsed work budget was not observed.");
            await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(error.Message).IsEqualTo(DeadlineDetail);
        }
    }
}
