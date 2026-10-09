using KeyLoad.Orleans;

namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeTextIncrementalCrashReplay
{
    private const int FirstPage = 0;
    internal static async Task<(TextMaintenanceCapabilityResult State, ProjectionBatchResult? Receipt)> FinishAsync(
        NativeTextIncrementalCrashRuntime runtime, TextIndexMaintenanceRequest request, CancellationToken token)
    {
        var state = await runtime.PhaseAsync(request, TextMaintenanceCapabilityKind.Begin, token: token);
        ProjectionBatchResult? receipt = null;
        if (state.OriginalCheckpointIntent is { } original)
        {
            await NativeTextIncrementalCheckpointEvidence.RecordCommandAsync(runtime, request, original, token);
            (state, receipt) = await CompleteIntentAsync(runtime, request, original, token);
        }
        for (var number = FirstPage; number < NativeTextIncrementalCrashProtocol.MaximumPages; number++)
        {
            var page = runtime.Database.ReadProjectionBatch(CrashFixtureValues.Principal,
                new(request.Consumer, ThroughSequence: state.ReplayUpperSequence));
            if (page.Entries.IsEmpty && state.ThroughSequence == state.ReplayUpperSequence && state.IndexSha256 is not null)
            {
                if (page.HasMore)
                { throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid); }
                var complete = await runtime.PhaseAsync(request, TextMaintenanceCapabilityKind.Verify, token: token);
                await runtime.RetireSessionAsync();
                return (complete, receipt);
            }
            var intent = new CommitProjectionBatchRequest(Guid.NewGuid(), request.Consumer, page.Token, []);
            await NativeTextIncrementalCheckpointEvidence.RecordCommandAsync(runtime, request, intent, token);
            _ = await runtime.PhaseAsync(request, TextMaintenanceCapabilityKind.PreparePage, page, intent, token: token);
            (state, receipt) = await CompleteIntentAsync(runtime, request, intent, token);
            if (state.ThroughSequence == state.ReplayUpperSequence)
            {
                var complete = await runtime.PhaseAsync(request, TextMaintenanceCapabilityKind.Verify, token: token);
                await runtime.RetireSessionAsync();
                return (complete, receipt);
            }
        }
        throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
    }

    private static async Task<(TextMaintenanceCapabilityResult State, ProjectionBatchResult Receipt)> CompleteIntentAsync(
        NativeTextIncrementalCrashRuntime runtime, TextIndexMaintenanceRequest request,
        CommitProjectionBatchRequest original, CancellationToken token)
    {
        _ = await runtime.PhaseAsync(request, TextMaintenanceCapabilityKind.ApplyIntent, token: token);
        var actual = await runtime.CommitAsync<ProjectionBatchResult>(OperationKind.CommitProjectionBatch,
            original, original.CommandId, token);
        await NativeTextIncrementalCheckpointEvidence.RecordReceiptAsync(runtime, request, actual, token);
        var state = await runtime.PhaseAsync(request, TextMaintenanceCapabilityKind.SettleCheckpoint,
            acknowledged: actual, token: token);
        return (state, actual);
    }
}
