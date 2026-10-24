using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.RecoveryTests.Search;

internal static class TextIncrementalCrashReplay
{
    internal static async Task<(TextMaintenanceCapabilityResult State, ProjectionBatchResult? Receipt)> FinishAsync(
        TextIncrementalCrashRuntime runtime, TextIndexMaintenanceRequest request, CancellationToken token)
    {
        var state = await runtime.PhaseAsync(request, TextMaintenanceCapabilityKind.Begin, token: token);
        ProjectionBatchResult? receipt = null;
        if (state.OriginalCheckpointIntent is { } original)
        {
            await TextIncrementalCheckpointEvidence.RecordCommandAsync(runtime, request, original, token);
            (state, receipt) = await CompleteIntentAsync(runtime, request, original, token);
        }
        for (var number = 0; number < TextIncrementalCrashProtocol.MaximumPages; number++)
        {
            var page = runtime.Database.ReadProjectionBatch(CrashFixtureValues.Principal,
                new(request.Consumer, ThroughSequence: state.ReplayUpperSequence));
            if (page.Entries.IsEmpty && state.ThroughSequence == state.ReplayUpperSequence && state.IndexSha256 is not null)
            {
                if (page.HasMore)
                {
                    throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
                }
                var complete = await runtime.PhaseAsync(request, TextMaintenanceCapabilityKind.Verify, token: token);
                await runtime.RetireSessionAsync();
                return (complete, receipt);
            }
            var intent = new CommitProjectionBatchRequest(Guid.NewGuid(), request.Consumer, page.Token, []);
            await TextIncrementalCheckpointEvidence.RecordCommandAsync(runtime, request, intent, token);
            _ = await runtime.PhaseAsync(request, TextMaintenanceCapabilityKind.PreparePage, page, intent, token: token);
            (state, receipt) = await CompleteIntentAsync(runtime, request, intent, token);
            if (state.ThroughSequence == state.ReplayUpperSequence)
            {
                var complete = await runtime.PhaseAsync(request, TextMaintenanceCapabilityKind.Verify, token: token);
                await runtime.RetireSessionAsync();
                return (complete, receipt);
            }
        }
        throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
    }

    private static async Task<(TextMaintenanceCapabilityResult State, ProjectionBatchResult Receipt)> CompleteIntentAsync(
        TextIncrementalCrashRuntime runtime, TextIndexMaintenanceRequest request,
        CommitProjectionBatchRequest original, CancellationToken token)
    {
        _ = await runtime.PhaseAsync(request, TextMaintenanceCapabilityKind.ApplyIntent, token: token);
        var actual = await runtime.CommitAsync<ProjectionBatchResult>(OperationKind.CommitProjectionBatch,
            original, original.CommandId, token);
        await TextIncrementalCheckpointEvidence.RecordReceiptAsync(runtime, request, actual, token);
        var state = await runtime.PhaseAsync(request, TextMaintenanceCapabilityKind.SettleCheckpoint,
            acknowledged: actual, token: token);
        return (state, actual);
    }
}
