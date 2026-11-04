using KeyLoad.Clustering.Search;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

internal static class OnlineTextParentFlow
{
    internal static async Task<OnlineTextIndexMaintenanceResult> RunAsync(OnlineTextChildCalls children,
        OnlineTextIndexMaintenanceRequest request, ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var token = writer.CancellationToken;
        var original = await children.CapabilityAsync(request, OnlineTextCapabilityKind.ResolveOriginal, token);
        if (original.OriginalResult is { } retained)
        {
            return retained;
        }
        _ = await children.CapabilityAsync(request, OnlineTextCapabilityKind.Capture, token);
        await children.ObserveCapturedAsync(token);
        await OnlineTextProgress.WriteAsync(writer, children.ParentRequestId, OnlineTextPhase.Captured);
        var state = await children.CapabilityAsync(request, OnlineTextCapabilityKind.Seed, token);
        await OnlineTextProgress.WriteAsync(writer, children.ParentRequestId, OnlineTextPhase.Seeded);
        _ = await OnlineTextReplay.RunAsync(children, request, state, writer);
        await OnlineTextProgress.WriteAsync(writer, children.ParentRequestId, OnlineTextPhase.CaughtUp);
        _ = await children.CapabilityAsync(request, OnlineTextCapabilityKind.Validate, token);
        await OnlineTextProgress.WriteAsync(writer, children.ParentRequestId, OnlineTextPhase.Validated);
        state = await children.CapabilityAsync(request, OnlineTextCapabilityKind.IssuePublication, token);
        var issued = state.IssuedPublication ?? throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        var result = await children.PublishAsync(issued, token);
        var reconciled = await children.CapabilityAsync(request, OnlineTextCapabilityKind.ReconcileCommitted, token);
        RequireOriginal(result, reconciled.OriginalResult);
        await OnlineTextProgress.WriteAsync(writer, children.ParentRequestId, OnlineTextPhase.Published);
        await OnlineTextProgress.WriteAsync(writer, children.ParentRequestId, OnlineTextPhase.RetirementMarked);
        return result;
    }

    private static void RequireOriginal(OnlineTextIndexMaintenanceResult result,
        OnlineTextIndexMaintenanceResult? original)
    {
        if (original is null
            || !OrleansSerialization.Serialize(result).AsSpan().SequenceEqual(OrleansSerialization.Serialize(original)))
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, TextIndexMaintenanceProtocol.Interrupted);
        }
    }
}
