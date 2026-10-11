using KeyLoad.Features.InternalSerialization;
using KeyLoad.Orleans;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Server.Features.EventStreams;

/// <summary>Projects actual same-owner Tail deliveries through the native joined bounded CQRS producer.</summary>
internal static class EventFeedTailPublicStream
{
    private const int CurrentProgressVersion = 1;
    private const string Invalid = "The event feed Tail returned an invalid public progress frame.";
    internal static IAsyncEnumerable<CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult>> Create(
        HttpContext context, EventFeedControlRequest request)
    {
        var commandId = EventFeedParentArguments.Require(request, EventFeedControlAction.Tail);
        return CqrsStream.Create<EventFeedTailProgress, EventFeedControlResult>(
            writer => ExecuteAsync(context, request, commandId, writer), context.RequestAborted);
    }

    private static async ValueTask<Result<EventFeedControlResult>> ExecuteAsync(HttpContext context,
        EventFeedControlRequest request, Guid commandId,
        ICqrsStreamWriter<EventFeedTailProgress, EventFeedControlResult> writer)
    {
        try
        {
            var result = await CanonicalOperationGateway.ExecuteTailResultAsync(context, commandId,
                NativeSerialization.Serialize(request, NativeValidationProfile.PublicInputElements),
                (frame, token) => ReportAsync(writer, frame, token), writer.CancellationToken).ConfigureAwait(false);
            return Result<EventFeedControlResult>.Succeed(result);
        }
        catch (KeyLoadException refusal) { return Result<EventFeedControlResult>.Fail(refusal.ToProblem()); }
    }

    private static ValueTask ReportAsync(ICqrsStreamWriter<EventFeedTailProgress, EventFeedControlResult> writer,
        GrainRequestProgress frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return frame.EventFeedTail is { Version: CurrentProgressVersion } value
            ? writer.ProgressAsync(value) : ValueTask.FromException(Errors.Fail(ErrorCode.RecoveryRequired, Invalid));
    }

    internal static IAsyncEnumerable<CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult>> AdmitAsync(
        HttpContext context, IAsyncEnumerable<CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult>> source,
        CancellationToken cancellationToken = default)
        => EventFeedTailPublicAdmission.ReadAsync(source, EventFeedTailJsonGrant.Create(context), cancellationToken);
}
