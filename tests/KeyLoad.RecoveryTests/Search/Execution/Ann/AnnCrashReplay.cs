using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.RecoveryTests.Search;

internal static class AnnCrashReplay
{
    internal static async Task<(AnnMaintenanceCapabilityResult Result, ProjectionBatchResult Receipt)> FinishAsync(
        DatabaseEngine database, AnnCrashRuntime runtime, AnnCrashCommandOwner commands, AnnMaintenanceRequest request,
        AnnMaintenanceCapabilityResult began)
    {
        ProjectionBatchResult? receipt = null;
        if (began.OriginalCheckpointIntent is { } old)
        {
            receipt = AnnCrashState.CommitReplay(commands, old);
        }
        if (request.Mode == AnnMaintenanceMode.Restore)
        {
            _ = await AnnCrashState.PhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Load);
        }
        var upper = began.Source!.ThroughSequence;
        var finished = false;
        for (var number = 0; number < AnnCrashContract.MaximumPages; number++)
        {
            var page = AnnCrashOperations.Read(database, request, upper);
            if (page.ThroughSequence == page.Consumer.Checkpoint)
            {
                if (page.HasMore || page.ThroughSequence != upper || !page.Entries.IsEmpty)
                {
                    throw new InvalidOperationException(AnnCrashContract.Invalid);
                }
                finished = true;
                break;
            }
            var intent = new CommitProjectionBatchRequest(Guid.NewGuid(), request.Consumer, page.Token, []);
            _ = await AnnCrashState.PhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.ApplyPage, page);
            _ = await AnnCrashState.PhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.StagePage, intent: intent);
            receipt = AnnCrashState.CommitReplay(commands, intent);
            if (!page.HasMore)
            {
                finished = true;
                break;
            }
        }
        if (!finished || receipt is null)
        {
            throw new InvalidOperationException(AnnCrashContract.Invalid);
        }
        _ = await AnnCrashState.PhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Verify);
        var result = await AnnCrashState.PhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Publish);
        if (result.Pending || result.Count != AnnCrashContract.Count || string.IsNullOrEmpty(result.IndexSha256))
        {
            throw new InvalidOperationException(AnnCrashContract.Invalid);
        }
        return (result, receipt);
    }
}
