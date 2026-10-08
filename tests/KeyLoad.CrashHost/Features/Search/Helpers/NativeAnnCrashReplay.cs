using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeAnnCrashReplay
{
    private const int FirstPage = 0;
    internal static async Task<(AnnMaintenanceCapabilityResult Result, ProjectionBatchResult Receipt)> FinishAsync(
        DatabaseEngine database, NativeAnnCrashRuntime runtime, NativeAnnCrashCommandOwner commands, AnnMaintenanceRequest request,
        AnnMaintenanceCapabilityResult began)
    {
        ProjectionBatchResult? receipt = null;
        if (began.OriginalCheckpointIntent is { } old)
        { receipt = NativeAnnCrashState.CommitReplay(commands, old); }
        if (request.Mode == AnnMaintenanceMode.Restore)
        { _ = await NativeAnnCrashState.PhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Load); }
        var upper = began.Source!.ThroughSequence;
        var finished = false;
        for (var number = FirstPage; number < NativeAnnCrashContract.MaximumPages; number++)
        {
            var page = NativeAnnCrashOperations.Read(database, request, upper);
            if (page.ThroughSequence == page.Consumer.Checkpoint)
            {
                if (page.HasMore || page.ThroughSequence != upper || !page.Entries.IsEmpty)
                { throw new InvalidOperationException(NativeAnnCrashContract.Invalid); }
                finished = true;
                break;
            }
            var intent = new CommitProjectionBatchRequest(Guid.NewGuid(), request.Consumer, page.Token, []);
            _ = await NativeAnnCrashState.PhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.ApplyPage, page);
            _ = await NativeAnnCrashState.PhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.StagePage, intent: intent);
            receipt = NativeAnnCrashState.CommitReplay(commands, intent);
            if (!page.HasMore)
            { finished = true; break; }
        }
        if (!finished || receipt is null)
        { throw new InvalidOperationException(NativeAnnCrashContract.Invalid); }
        _ = await NativeAnnCrashState.PhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Verify);
        var result = await NativeAnnCrashState.PhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Publish);
        if (result.Pending || result.Count != NativeAnnCrashContract.Count || string.IsNullOrEmpty(result.IndexSha256))
        { throw new InvalidOperationException(NativeAnnCrashContract.Invalid); }
        return (result, receipt);
    }
}
