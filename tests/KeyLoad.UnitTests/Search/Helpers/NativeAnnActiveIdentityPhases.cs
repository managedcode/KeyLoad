using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeAnnActiveIdentityPhases
{
    private const int MaximumPages = 12;
    private const int FirstPage = 0;
    private const string InvalidUpper = "The real identity fixture did not reach its exact upper.";
    private const string PageBoundExceeded = "The real identity fixture exceeded the unchanged twelve-page bound.";

    internal static async Task<AnnMaintenanceResult> FinishAsync(TestDatabase database,
        NativeAnnMaintenanceTestRuntime runtime, AnnMaintenanceRequest request, long upper)
    {
        ProjectionBatchResult? checkpoint = null;
        CommitProjectionBatchRequest? empty = null;
        var complete = false;
        for (var number = FirstPage; number < MaximumPages; number++)
        {
            var page = NativeAnnMaintenanceTestData.Read(database, request, upper);
            if (page.ThroughSequence == page.Consumer.Checkpoint)
            {
                if (page.HasMore || page.ThroughSequence != upper || !page.Entries.IsEmpty)
                {
                    throw new InvalidOperationException(InvalidUpper);
                }
                empty = NativeAnnMaintenanceTestData.Intent(page);
                complete = true;
                break;
            }
            var intent = NativeAnnMaintenanceTestData.Intent(page);
            _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.ApplyPage, page);
            _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.StagePage, intent: intent);
            checkpoint = await NativeAnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent);
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
        _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Verify);
        var published = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Publish, intent: empty);
        if (empty is not null)
        {
            checkpoint = await NativeAnnMaintenancePhaseAssertions.CommitReplayAsync(database, empty);
        }
        return new(request.CommandId, request.Consumer, request.IndexGeneration, AnnMaintenancePhase.Completed,
            published.Source, published.Count, published.IndexSha256, checkpoint);
    }
}
