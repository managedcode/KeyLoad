using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnLifetimeNativePublication
{
    private const int FirstPage = 0;
    private const int MaximumPages = 3;
    private const int PageLimit = AnnLifetimeNativeProtocol.Count;
    private const string Incomplete = "The native ANN lifetime publication did not settle its bounded exact upper.";

    internal static async Task<AnnMaintenanceCapabilityResult> FinishAsync(TestDatabase database,
        NativeAnnMaintenanceTestRuntime runtime, AnnMaintenanceRequest request, long upper)
    {
        var completed = false;
        CommitProjectionBatchRequest? emptyIntent = null;
        for (var number = FirstPage; number < MaximumPages; number++)
        {
            var page = database.Database.ReadProjectionBatch(AnnProjectionPinTestSupport.Principal,
                new(request.Consumer, PageLimit, NativeAnnMaintenanceTestData.PageBytes, upper));
            if (page.ThroughSequence == page.Consumer.Checkpoint)
            {
                if (page.HasMore || page.ThroughSequence != upper || !page.Entries.IsEmpty)
                {
                    throw new InvalidOperationException(Incomplete);
                }
                if (request.Mode == AnnMaintenanceMode.Build)
                {
                    emptyIntent = NativeAnnMaintenanceTestData.Intent(page);
                }
                completed = true;
                break;
            }
            var intent = NativeAnnMaintenanceTestData.Intent(page);
            _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request,
                AnnMaintenanceCapabilityKind.ApplyPage, page);
            _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request,
                AnnMaintenanceCapabilityKind.StagePage, intent: intent);
            _ = await NativeAnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent);
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
        _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Verify);
        var published = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request,
            AnnMaintenanceCapabilityKind.Publish, intent: emptyIntent);
        if (emptyIntent is not null)
        {
            _ = await NativeAnnMaintenancePhaseAssertions.CommitReplayAsync(database, emptyIntent);
        }
        return published;
    }
}
