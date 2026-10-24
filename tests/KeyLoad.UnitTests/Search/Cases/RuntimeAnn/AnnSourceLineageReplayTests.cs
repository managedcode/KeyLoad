using KeyLoad.Clustering;
using KeyLoad.Server;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Search;

internal sealed class AnnSourceLineageReplayTests
{
    private const int InitialCount = 3;
    private const int HiddenCount = 2;
    private static readonly string[] RemainingIds = ["seed-00001", "seed-00002"];

    [Test]
    public async Task ActualSourcePatchAndProjectionTargetReappearOnlyThroughAdmittedCanonicalOutboxReplay()
    {
        using var database = AnnSeedTestSupport.Create(InitialCount);
        AnnLineageTestData.Seed(database);
        var request = AnnMaintenanceTestData.Pin(database);
        var failures = new List<Exception>();
        AnnMaintenanceCapabilityResult? publication = null;
        try
        {
            await using var runtime = new AnnMaintenanceTestRuntime(database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var built = await CompleteAsync(database, actual, request);
                await Assert.That(built.Count).IsEqualTo(InitialCount);
                await AnnLineageReplayOracle.AssertSeedAsync(AnnSeedTestSupport.Capture(database,
                    AnnProjectionPinTestSupport.Principal), AnnLineageReplayOracle.Stage.Initial);
                await actual.Owner.AbortAsync(actual.SessionId);
                AnnLineageTestData.ChangeSource(database);
                request = request with
                {
                    Mode = AnnMaintenanceMode.Restore
                };
                var hidden = await CompleteAsync(database, actual, request);
                await Assert.That(hidden.Count).IsEqualTo(HiddenCount);
                var seed = AnnSeedTestSupport.Capture(database, AnnProjectionPinTestSupport.Principal);
                await AnnLineageReplayOracle.AssertSeedAsync(seed, AnnLineageReplayOracle.Stage.Hidden);
                await Assert.That(seed.Records.Select(row => row.DocumentId)).IsEquivalentTo(RemainingIds, CollectionOrdering.Matching);
                await actual.Owner.AbortAsync(actual.SessionId);
                AnnLineageTestData.Reproject(database);
                var healthy = await CompleteAsync(database, actual, request);
                await Assert.That(healthy.Count).IsEqualTo(InitialCount);
                var restored = AnnSeedTestSupport.Capture(database, AnnProjectionPinTestSupport.Principal);
                await Assert.That(restored.Records[0].DocumentId).IsEqualTo(AnnSeedTestSupport.Id(0));
                await Assert.That(restored.Records[0].Values).IsEquivalentTo(new float[] { 4, 3, 2 }, CollectionOrdering.Matching);
                await Assert.That(healthy.Source!.CorpusSha256).IsEqualTo(restored.CorpusSha256);
                await Assert.That(healthy.IndexSha256).IsNotEqualTo(hidden.IndexSha256);
                await AnnLineageReplayOracle.AssertSeedAsync(restored, AnnLineageReplayOracle.Stage.Reprojected);
                publication = healthy;
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        await AnnLineageColdContinuation.RunAsync(database, request, publication
            ?? throw new InvalidOperationException("The execution lineage publication did not complete."));
    }

    private static async Task<AnnMaintenanceCapabilityResult> CompleteAsync(TestDatabase database,
        AnnMaintenanceTestRuntime runtime, AnnMaintenanceRequest request)
    {
        var began = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Begin);
        if (request.Mode == AnnMaintenanceMode.Restore)
        {
            _ = await AnnMaintenancePhaseAssertions.CommitReplayAsync(database, began.OriginalCheckpointIntent!);
            _ = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Load);
        }
        return await AnnMaintenancePhaseAssertions.FinishAsync(database, runtime, request, began.Source!.ThroughSequence);
    }
}
