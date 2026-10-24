using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Search;

internal static class AnnActiveIdentityPhases
{
    private const int MaximumPages = 12;
    private const string InvalidUpper = "The real identity fixture did not reach its exact upper.";
    private const string PageBoundExceeded = "The real identity fixture exceeded the unchanged twelve-page bound.";

    internal static async Task<AnnMaintenanceResult> FinishAsync(TestDatabase database,
        AnnMaintenanceTestRuntime runtime, AnnMaintenanceRequest request, long upper)
    {
        ProjectionBatchResult? checkpoint = null;
        CommitProjectionBatchRequest? empty = null;
        var complete = false;
        for (var number = 0; number < MaximumPages; number++)
        {
            var page = AnnMaintenanceTestData.Read(database, request, upper);
            if (page.ThroughSequence == page.Consumer.Checkpoint)
            {
                if (page.HasMore || page.ThroughSequence != upper || !page.Entries.IsEmpty)
                {
                    throw new InvalidOperationException(InvalidUpper);
                }
                empty = AnnMaintenanceTestData.Intent(page);
                complete = true;
                break;
            }
            var intent = AnnMaintenanceTestData.Intent(page);
            _ = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.ApplyPage, page);
            _ = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.StagePage, intent: intent);
            checkpoint = await AnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent);
            if (!page.HasMore)
            {
                complete = true;
                break;
            }
        }
        if (!complete)
        {
            throw new InvalidOperationException(PageBoundExceeded);
        }
        _ = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Verify);
        var published = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Publish, intent: empty);
        if (empty is not null)
        {
            checkpoint = await AnnMaintenancePhaseAssertions.CommitReplayAsync(database, empty);
        }
        return new(request.CommandId, request.Consumer, request.IndexGeneration, AnnMaintenancePhase.Completed,
            published.Source, published.Count, published.IndexSha256, checkpoint);
    }
}
