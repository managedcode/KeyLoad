using KeyLoad.Clustering;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnMaintenancePhaseAssertions
{
    private const int MaximumPages = 12;
    internal static async Task RejectAtomicParentAsync(TestDatabase database, AnnMaintenanceRequest request)
    {
        var state = AnnMaintenanceTestData.Snapshot(database);
        var position = database.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.CreateCommand(
            OperationKind.MaintainAnnIndex, request.CommandId, AnnProjectionPinTestSupport.Principal,
            database.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request)));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(AnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(state, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    internal static async Task<AnnMaintenanceCapabilityResult> UnchangedPhaseAsync(TestDatabase database,
        AnnMaintenanceTestRuntime runtime, AnnMaintenanceRequest request, AnnMaintenanceCapabilityKind kind,
        ProjectionBatch? page = null, CommitProjectionBatchRequest? intent = null)
    {
        var state = AnnMaintenanceTestData.Snapshot(database);
        var position = database.Store.Position;
        var result = await runtime.PhaseAsync(database, request, kind, page, intent);
        await Assert.That(AnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(state, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        return result;
    }
    internal static async Task<ProjectionBatchResult> CommitReplayAsync(TestDatabase database, CommitProjectionBatchRequest intent)
    {
        var applied = AnnMaintenanceTestData.Commit(database, intent);
        var state = AnnMaintenanceTestData.Snapshot(database);
        var position = database.Store.Position;
        var replay = AnnMaintenanceTestData.Commit(database, intent);
        await Assert.That(OrleansSerialization.Serialize(replay)).IsEquivalentTo(OrleansSerialization.Serialize(applied), CollectionOrdering.Matching);
        await Assert.That(AnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(state, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        return applied;
    }
    internal static async Task<AnnMaintenanceCapabilityResult> FinishAsync(TestDatabase database,
        AnnMaintenanceTestRuntime runtime, AnnMaintenanceRequest request, long upper)
    {
        var completed = false;
        CommitProjectionBatchRequest? emptyIntent = null;
        for (var number = 0; number < MaximumPages; number++)
        {
            var page = AnnMaintenanceTestData.Read(database, request, upper);
            if (page.ThroughSequence == page.Consumer.Checkpoint)
            {
                if (page.HasMore || page.ThroughSequence != upper || !page.Entries.IsEmpty)
                {
                    throw new InvalidOperationException("The execution ANN empty admission does not settle its exact upper.");
                }
                if (request.Mode == AnnMaintenanceMode.Build)
                {
                    emptyIntent = AnnMaintenanceTestData.Intent(page);
                }
                completed = true;
                break;
            }
            var intent = AnnMaintenanceTestData.Intent(page);
            _ = await UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.ApplyPage, page);
            _ = await UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.StagePage, intent: intent);
            _ = await CommitReplayAsync(database, intent);
            if (!page.HasMore)
            {
                completed = true;
                break;
            }
        }
        if (!completed)
        {
            throw new InvalidOperationException("The execution ANN lifecycle fixture exceeds its page bound.");
        }
        _ = await UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Verify);
        var published = await UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Publish, intent: emptyIntent);
        if (emptyIntent is not null)
        {
            _ = await CommitReplayAsync(database, emptyIntent);
        }
        return published;
    }
}
