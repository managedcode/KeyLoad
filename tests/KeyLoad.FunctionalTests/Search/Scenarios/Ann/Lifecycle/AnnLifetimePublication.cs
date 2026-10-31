using KeyLoad.Clustering;

namespace KeyLoad.FunctionalTests.Search;

internal static class AnnLifetimePublication
{
    private const int MaximumPages = 3;
    private const int PageLimit = AnnLifetimeProtocol.Count;
    private const string Incomplete = "The execution ANN lifetime publication did not settle its bounded exact upper.";

    internal static async Task<AnnMaintenanceCapabilityResult> FinishAsync(TestDatabase database,
        AnnMaintenanceTestRuntime runtime, AnnMaintenanceRequest request, long upper)
    {
        var completed = false;
        CommitProjectionBatchRequest? emptyIntent = null;
        for (var number = 0; number < MaximumPages; number++)
        {
            var page = database.Database.ReadProjectionBatch(AnnProjectionPinTestSupport.Principal,
                new(request.Consumer, PageLimit, upper));
            if (page.ThroughSequence == page.Consumer.Checkpoint)
            {
                if (page.HasMore || page.ThroughSequence != upper || !page.Entries.IsEmpty)
                {
                    throw new InvalidOperationException(Incomplete);
                }
                if (request.Mode == AnnMaintenanceMode.Build)
                {
                    emptyIntent = AnnMaintenanceTestData.Intent(page);
                }
                completed = true;
                break;
            }
            var intent = AnnMaintenanceTestData.Intent(page);
            _ = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request,
                AnnMaintenanceCapabilityKind.ApplyPage, page);
            _ = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request,
                AnnMaintenanceCapabilityKind.StagePage, intent: intent);
            _ = await AnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent);
            if (!page.HasMore)
            {
                if (page.ThroughSequence != upper)
                {
                    throw new InvalidOperationException(Incomplete);
                }
                completed = true;
                break;
            }
        }
        if (!completed)
        {
            throw new InvalidOperationException(Incomplete);
        }
        _ = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Verify);
        var published = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request,
            AnnMaintenanceCapabilityKind.Publish, intent: emptyIntent);
        if (emptyIntent is not null)
        {
            _ = await AnnMaintenancePhaseAssertions.CommitReplayAsync(database, emptyIntent);
        }
        return published;
    }
}
