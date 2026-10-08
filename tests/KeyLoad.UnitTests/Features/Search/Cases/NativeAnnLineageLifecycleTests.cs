using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeAnnLineageLifecycleTests
{
    private const int LiteralCount = 3;

    [Test]
    public async Task RealProjectionTargetIsPinnedAndPolicyHideRestorePermanentlyInvalidatesUntilExplicitRebuild()
    {
        using var database = AnnSeedTestSupport.Create(LiteralCount);
        NativeAnnLineageTestData.Seed(database);
        var request = NativeAnnMaintenanceTestData.Pin(database);
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new NativeAnnMaintenanceTestRuntime(database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var began = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Begin);
                var built = await NativeAnnMaintenancePhaseAssertions.FinishAsync(database, actual, request, began.Source!.ThroughSequence);
                await Assert.That(built.Count).IsEqualTo(LiteralCount);
                await actual.Owner.AbortAsync(actual.SessionId);
                NativeAnnLineageTestData.Policy(database, hidden: true);
                await RejectRestoreAsync(database, actual, request);
                NativeAnnLineageTestData.Policy(database, hidden: false);
                await RejectRestoreAsync(database, actual, request);
                var healthy = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Begin);
                var rebuilt = await NativeAnnMaintenancePhaseAssertions.FinishAsync(database, actual, request, healthy.Source!.ThroughSequence);
                await Assert.That(rebuilt.Count).IsEqualTo(LiteralCount);
                await Assert.That(rebuilt.Source!.SchemaVersion).IsGreaterThan(built.Source!.SchemaVersion);
                await Assert.That(rebuilt.Source.DependencySha256).IsNotEqualTo(built.Source.DependencySha256);
                await NativeAnnLineageLiteralAssertions.AssertAsync(database, built.Source, rebuilt.Source);
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RejectRestoreAsync(TestDatabase database, NativeAnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest request)
    {
        var before = NativeAnnMaintenanceTestData.Snapshot(database);
        var position = database.Store.Position;
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => runtime.PhaseAsync(database,
            request with { Mode = AnnMaintenanceMode.Restore }, AnnMaintenanceCapabilityKind.Begin))
            ?? throw new InvalidOperationException("The native policy transition did not reject the obsolete generation.");
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(failure.Message).IsEqualTo(NativeAnnProtocol.MissingDependencyHistory);
        await Assert.That(NativeAnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }
}
