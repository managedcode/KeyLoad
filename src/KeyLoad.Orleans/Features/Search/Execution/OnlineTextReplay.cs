using KeyLoad.Orleans.Features.Search;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Orleans;

internal static class OnlineTextReplay
{
    private const int FirstReplayPage = 0;
    internal static async Task<OnlineTextCapabilityResult> RunAsync(OnlineTextChildCalls children,
        OnlineTextIndexMaintenanceRequest request, OnlineTextCapabilityResult state, int maximumPages,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var token = writer.CancellationToken;
        for (var number = FirstReplayPage; number < maximumPages; number++)
        {
            var upper = state.CurrentCut?.ThroughSequence
                ?? throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
            var page = await children.ReadPageAsync(new(request.Consumer, children.Database.Limits.MaxResults,
                children.Database.Limits.MaxProjectionBatchBytes, upper), token).ConfigureAwait(true);
            if (page.ThroughSequence > upper || page.Consumer.Consumer != request.Consumer
                || page.Consumer.Definition.IndexGeneration != request.ConsumerGeneration || page.Consumer.Released)
            { throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest); }
            var intent = new CommitProjectionBatchRequest(OnlineTextCheckpointIdentity.Create(request.CommandId,
                page.ThroughSequence), request.Consumer, page.Token, []);
            _ = await children.CapabilityAsync(request, OnlineTextCapabilityKind.PreparePage, token,
                page: page, intent: intent).ConfigureAwait(true);
            _ = await children.CapabilityAsync(request, OnlineTextCapabilityKind.ApplyIntent, token).ConfigureAwait(true);
            var acknowledged = await children.CheckpointAsync(intent, token).ConfigureAwait(true);
            state = await children.CapabilityAsync(request, OnlineTextCapabilityKind.SettleCheckpoint, token,
                intent: intent, acknowledged: acknowledged).ConfigureAwait(true);
            if (state.CurrentCut is not { } current || state.ThroughSequence > current.ThroughSequence)
            { throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest); }
            if (state.ThroughSequence == current.ThroughSequence
                && acknowledged.Checkpoint == current.ThroughSequence)
            { return state; }
        }
        throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
    }
}
