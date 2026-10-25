using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnLineageLifecycleTests
{
    private const int LiteralCount = 3;

    [Test]
    public async Task RealProjectionTargetIsPinnedAndPolicyHideRestorePermanentlyInvalidatesUntilExplicitRebuild()
    {
        using var database = AnnSeedTestSupport.Create(LiteralCount);
        AnnLineageTestData.Seed(database);
        var request = AnnMaintenanceTestData.Pin(database);
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new AnnMaintenanceTestRuntime(database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var began = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Begin);
                var built = await AnnMaintenancePhaseAssertions.FinishAsync(database, actual, request, began.Source!.ThroughSequence);
                await Assert.That(built.Count).IsEqualTo(LiteralCount);
                await actual.Owner.AbortAsync(actual.SessionId);
                AnnLineageTestData.Policy(database, hidden: true);
                await RejectRestoreAsync(database, actual, request);
                AnnLineageTestData.Policy(database, hidden: false);
                await RejectRestoreAsync(database, actual, request);
                var healthy = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Begin);
                var rebuilt = await AnnMaintenancePhaseAssertions.FinishAsync(database, actual, request, healthy.Source!.ThroughSequence);
                await Assert.That(rebuilt.Count).IsEqualTo(LiteralCount);
                await Assert.That(rebuilt.Source!.SchemaVersion).IsGreaterThan(built.Source!.SchemaVersion);
                await Assert.That(rebuilt.Source.DependencySha256).IsNotEqualTo(built.Source.DependencySha256);
                await AnnLineageLiteralAssertions.AssertAsync(database, built.Source, rebuilt.Source);
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RejectRestoreAsync(TestDatabase database, AnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest request)
    {
        var before = AnnMaintenanceTestData.Snapshot(database);
        var position = database.Store.Position;
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => runtime.PhaseAsync(database,
            request with
            {
                Mode = AnnMaintenanceMode.Restore
            }, AnnMaintenanceCapabilityKind.Begin))
            ?? throw new InvalidOperationException("The execution policy transition did not reject the obsolete generation.");
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(failure.Message).IsEqualTo(AnnProtocol.MissingDependencyHistory);
        await Assert.That(AnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await runtime.Owner.AbortAsync(runtime.SessionId);
    }
}
