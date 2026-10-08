using KeyLoad.Orleans;
using KeyLoad.Server;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeAnnPendingLifecycleTests
{
    private const int LiteralCount = 3;
    private const long Advance = 1;

    [Test]
    public async Task ActualPendingPageSurvivesJoinedOwnerReopenAndCanonicalAckReplayBeforeCompletePublication()
    {
        using var database = AnnSeedTestSupport.Create(LiteralCount);
        var request = NativeAnnMaintenanceTestData.Pin(database);
        var prepared = await PrepareAndAcknowledgeAsync(database, request);
        request = request with { Mode = AnnMaintenanceMode.Restore };
        var completed = await RestoreAndPublishAsync(database, request, prepared.Upper, prepared.Intent);
        await NativeAnnPublishedImageAssertions.AssertAsync(database, request, completed.IndexSha256!);
        await AssertNativeRestoreAsync(database, request, completed.IndexSha256!);
    }

    private static async Task<(long Upper, CommitProjectionBatchRequest Intent)> PrepareAndAcknowledgeAsync(
        TestDatabase database, AnnMaintenanceRequest request)
    {
        long upper = 0;
        CommitProjectionBatchRequest? intent = null;
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new NativeAnnMaintenanceTestRuntime(database);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await NativeAnnMaintenancePhaseAssertions.RejectAtomicParentAsync(database, request);
                var began = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Begin);
                upper = began.Source!.ThroughSequence;
                var first = NativeAnnMaintenanceTestData.Read(database, request, upper);
                await Assert.That(first.HasMore).IsTrue();
                intent = NativeAnnMaintenanceTestData.Intent(first);
                _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.ApplyPage, first);
                var pending = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.StagePage, intent: intent);
                await Assert.That(pending.Pending).IsTrue();
                await Assert.That(pending.ThroughSequence).IsEqualTo(first.ThroughSequence);
                _ = await NativeAnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent);
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        return (upper, intent ?? throw new InvalidOperationException("The native pending fixture did not retain its original checkpoint intent."));
    }

    private static async Task<AnnMaintenanceCapabilityResult> RestoreAndPublishAsync(TestDatabase database,
        AnnMaintenanceRequest request, long upper, CommitProjectionBatchRequest intent)
    {
        AnnMaintenanceCapabilityResult? completed = null;
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new NativeAnnMaintenanceTestRuntime(database);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var resumed = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Begin);
                await Assert.That(NativeSerialization.Serialize(resumed.OriginalCheckpointIntent!)).IsEquivalentTo(NativeSerialization.Serialize(intent), CollectionOrdering.Matching);
                _ = await NativeAnnMaintenancePhaseAssertions.CommitReplayAsync(database, resumed.OriginalCheckpointIntent!);
                _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Load);
                completed = await NativeAnnMaintenancePhaseAssertions.FinishAsync(database, runtime, request, upper);
                await Assert.That(completed.Pending).IsFalse();
                await Assert.That(completed.Count).IsEqualTo(LiteralCount);
                await Assert.That(completed.Source!.ThroughSequence).IsEqualTo(upper);
                await Assert.That(completed.IndexSha256).IsNotNull();
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        return completed ?? throw new InvalidOperationException("The native pending fixture did not complete its original publication.");
    }

    private static async Task AssertNativeRestoreAsync(TestDatabase database, AnnMaintenanceRequest request, string digest)
    {
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new NativeAnnMaintenanceTestRuntime(database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(() => RestoreAndContinueAsync(database, actual, request, digest), failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RestoreAndContinueAsync(TestDatabase database, NativeAnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest request, string digest)
    {
        var began = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Begin);
        _ = await NativeAnnMaintenancePhaseAssertions.CommitReplayAsync(database, began.OriginalCheckpointIntent!);
        _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Load);
        _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Verify);
        var restored = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Publish);
        await Assert.That(restored.IndexSha256).IsEqualTo(digest);
        await Assert.That(restored.Count).IsEqualTo(LiteralCount);
        AnnSeedTestSupport.CommitVector(database, AnnSeedTestSupport.Id(0), [11.25f, -2.5f, 0.75f]);
        await Assert.That(AnnProjectionPinTestSupport.Tail(database)).IsEqualTo(restored.Source!.ThroughSequence + Advance);
    }
}
