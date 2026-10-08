using KeyLoad.Orleans;
using KeyLoad.Server;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeAnnSourceLineageReplayTests
{
    private const int InitialCount = 3;
    private const int HiddenCount = 2;
    private static readonly string[] RemainingIds = ["seed-00001", "seed-00002"];

    [Test]
    public async Task ActualSourcePatchAndNativeProjectionTargetReappearOnlyThroughAdmittedCanonicalOutboxReplay()
    {
        using var database = AnnSeedTestSupport.Create(InitialCount);
        NativeAnnLineageTestData.Seed(database);
        var request = NativeAnnMaintenanceTestData.Pin(database);
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new NativeAnnMaintenanceTestRuntime(database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var built = await CompleteAsync(database, actual, request);
                await Assert.That(built.Count).IsEqualTo(InitialCount);
                await actual.Owner.AbortAsync(actual.SessionId);
                NativeAnnLineageTestData.ChangeSource(database);
                request = request with { Mode = AnnMaintenanceMode.Restore };
                var hidden = await CompleteAsync(database, actual, request);
                await Assert.That(hidden.Count).IsEqualTo(HiddenCount);
                var seed = AnnSeedTestSupport.Capture(database, AnnProjectionPinTestSupport.Principal);
                await Assert.That(seed.Records.Select(row => row.DocumentId)).IsEquivalentTo(RemainingIds, CollectionOrdering.Matching);
                await actual.Owner.AbortAsync(actual.SessionId);
                NativeAnnLineageTestData.Reproject(database);
                var healthy = await CompleteAsync(database, actual, request);
                await Assert.That(healthy.Count).IsEqualTo(InitialCount);
                var restored = AnnSeedTestSupport.Capture(database, AnnProjectionPinTestSupport.Principal);
                await Assert.That(restored.Records[0].DocumentId).IsEqualTo(AnnSeedTestSupport.Id(0));
                await Assert.That(restored.Records[0].Values).IsEquivalentTo(new float[] { 4, 3, 2 }, CollectionOrdering.Matching);
                await Assert.That(healthy.Source!.CorpusSha256).IsEqualTo(restored.CorpusSha256);
                await Assert.That(healthy.IndexSha256).IsNotEqualTo(hidden.IndexSha256);
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<AnnMaintenanceCapabilityResult> CompleteAsync(TestDatabase database,
        NativeAnnMaintenanceTestRuntime runtime, AnnMaintenanceRequest request)
    {
        var began = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Begin);
        if (request.Mode == AnnMaintenanceMode.Restore)
        {
            _ = await NativeAnnMaintenancePhaseAssertions.CommitReplayAsync(database, began.OriginalCheckpointIntent!);
            _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Load);
        }
        return await NativeAnnMaintenancePhaseAssertions.FinishAsync(database, runtime, request, began.Source!.ThroughSequence);
    }
}
