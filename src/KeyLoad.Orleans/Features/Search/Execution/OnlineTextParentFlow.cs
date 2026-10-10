using KeyLoad.Orleans.Features.Search;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Orleans;

internal static class OnlineTextParentFlow
{
    internal static async Task<OnlineTextIndexMaintenanceResult> RunAsync(OnlineTextChildCalls children,
        OnlineTextIndexMaintenanceRequest request, OnlineTextFrameAccounting frames,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, TextIndexMaintenanceOptions options)
    {
        if (!options.IsValid())
        { throw Errors.Fail(ErrorCode.Validation, TextIndexMaintenanceOptions.ValidationMessage); }
        var token = writer.CancellationToken;
        var original = await children.CapabilityAsync(request, OnlineTextCapabilityKind.ResolveOriginal, token)
            .ConfigureAwait(true);
        if (original.OriginalResult is { } retained)
        { return retained; }
        _ = await children.CapabilityAsync(request, OnlineTextCapabilityKind.Capture, token).ConfigureAwait(true);
        await children.ObserveCapturedAsync(token).ConfigureAwait(true);
        await OnlineTextProgress.WriteAsync(writer, frames, children.ParentRequestId, OnlineTextPhase.Captured).ConfigureAwait(true);
        var state = await children.CapabilityAsync(request, OnlineTextCapabilityKind.Seed, token).ConfigureAwait(true);
        await OnlineTextProgress.WriteAsync(writer, frames, children.ParentRequestId, OnlineTextPhase.Seeded).ConfigureAwait(true);
        _ = await OnlineTextReplay.RunAsync(children, request, state, options.MaximumReplayPages, writer).ConfigureAwait(true);
        await OnlineTextProgress.WriteAsync(writer, frames, children.ParentRequestId, OnlineTextPhase.CaughtUp).ConfigureAwait(true);
        _ = await children.CapabilityAsync(request, OnlineTextCapabilityKind.Validate, token).ConfigureAwait(true);
        await OnlineTextProgress.WriteAsync(writer, frames, children.ParentRequestId, OnlineTextPhase.Validated).ConfigureAwait(true);
        state = await children.CapabilityAsync(request, OnlineTextCapabilityKind.IssuePublication, token).ConfigureAwait(true);
        var issued = state.IssuedPublication ?? throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        var result = await children.PublishAsync(issued, token).ConfigureAwait(true);
        var reconciled = await children.CapabilityAsync(request, OnlineTextCapabilityKind.ReconcileCommitted, token)
            .ConfigureAwait(true);
        RequireOriginal(result, reconciled.OriginalResult, children.Database.Limits.MaxQueryReadBytes);
        await OnlineTextProgress.WriteAsync(writer, frames, children.ParentRequestId, OnlineTextPhase.Published).ConfigureAwait(true);
        await OnlineTextProgress.WriteAsync(writer, frames, children.ParentRequestId, OnlineTextPhase.RetirementMarked).ConfigureAwait(true);
        return result;
    }

    private static void RequireOriginal(OnlineTextIndexMaintenanceResult result,
        OnlineTextIndexMaintenanceResult? original, long maximumBytes)
    {
        if (original is null || NativeSerialization.Measure(result) > maximumBytes
            || NativeSerialization.Measure(original) > maximumBytes
            || !NativeSerialization.Serialize(result).AsSpan().SequenceEqual(NativeSerialization.Serialize(original)))
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, TextIndexMaintenanceProtocol.Interrupted); }
    }
}
