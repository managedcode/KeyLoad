using KeyLoad.Clustering;
using KeyLoad.Server;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnPendingLifecycleTests
{
    private const int LiteralCount = 3;
    private const long Advance = 1;

    [Test]
    public async Task ActualPendingPageSurvivesJoinedOwnerReopenAndCanonicalAckReplayBeforeCompletePublication()
    {
        using var database = AnnSeedTestSupport.Create(LiteralCount);
        var request = AnnMaintenanceTestData.Pin(database);
        var prepared = await PrepareAndAcknowledgeAsync(database, request);
        request = request with
        {
            Mode = AnnMaintenanceMode.Restore
        };
        var completed = await RestoreAndPublishAsync(database, request, prepared.Upper, prepared.Intent);
        await AnnPublishedImageAssertions.AssertAsync(database, request, completed.IndexSha256!);
        await AssertRestoreAsync(database, request, completed.IndexSha256!);
    }

    private static async Task<(long Upper, CommitProjectionBatchRequest Intent)> PrepareAndAcknowledgeAsync(
        TestDatabase database, AnnMaintenanceRequest request)
    {
        long upper = 0;
        CommitProjectionBatchRequest? intent = null;
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new AnnMaintenanceTestRuntime(database);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await AnnMaintenancePhaseAssertions.RejectAtomicParentAsync(database, request);
                var began = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Begin);
                upper = began.Source!.ThroughSequence;
                var first = AnnMaintenanceTestData.Read(database, request, upper);
                await Assert.That(first.HasMore).IsTrue();
                intent = AnnMaintenanceTestData.Intent(first);
                _ = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.ApplyPage, first);
                var pending = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.StagePage, intent: intent);
                await Assert.That(pending.Pending).IsTrue();
                await Assert.That(pending.ThroughSequence).IsEqualTo(first.ThroughSequence);
                _ = await AnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent);
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        return (upper, intent ?? throw new InvalidOperationException("The execution pending fixture did not retain its original checkpoint intent."));
    }

    private static async Task<AnnMaintenanceCapabilityResult> RestoreAndPublishAsync(TestDatabase database,
        AnnMaintenanceRequest request, long upper, CommitProjectionBatchRequest intent)
    {
        AnnMaintenanceCapabilityResult? completed = null;
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new AnnMaintenanceTestRuntime(database);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var resumed = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Begin);
                await Assert.That(OrleansSerialization.Serialize(resumed.OriginalCheckpointIntent!)).IsEquivalentTo(OrleansSerialization.Serialize(intent), CollectionOrdering.Matching);
                _ = await AnnMaintenancePhaseAssertions.CommitReplayAsync(database, resumed.OriginalCheckpointIntent!);
                _ = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Load);
                completed = await AnnMaintenancePhaseAssertions.FinishAsync(database, runtime, request, upper);
                await Assert.That(completed.Pending).IsFalse();
                await Assert.That(completed.Count).IsEqualTo(LiteralCount);
                await Assert.That(completed.Source!.ThroughSequence).IsEqualTo(upper);
                await Assert.That(completed.IndexSha256).IsNotNull();
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        return completed ?? throw new InvalidOperationException("The execution pending fixture did not complete its original publication.");
    }

    private static async Task AssertRestoreAsync(TestDatabase database, AnnMaintenanceRequest request, string digest)
    {
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new AnnMaintenanceTestRuntime(database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(() => RestoreAndContinueAsync(database, actual, request, digest), failures);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RestoreAndContinueAsync(TestDatabase database, AnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest request, string digest)
    {
        var began = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Begin);
        _ = await AnnMaintenancePhaseAssertions.CommitReplayAsync(database, began.OriginalCheckpointIntent!);
        _ = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Load);
        _ = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Verify);
        var restored = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Publish);
        await Assert.That(restored.IndexSha256).IsEqualTo(digest);
        await Assert.That(restored.Count).IsEqualTo(LiteralCount);
        AnnSeedTestSupport.CommitVector(database, AnnSeedTestSupport.Id(0), [11.25f, -2.5f, 0.75f]);
        await Assert.That(AnnProjectionPinTestSupport.Tail(database)).IsEqualTo(restored.Source!.ThroughSequence + Advance);
    }
}
