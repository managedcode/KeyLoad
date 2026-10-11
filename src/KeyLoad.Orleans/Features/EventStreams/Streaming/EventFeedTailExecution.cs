using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Streams;

namespace KeyLoad.Orleans;

internal static class EventFeedTailExecution
{
    private const int Version = 1;
    private const int NoDeliveries = 0;
    private const string InvalidTail = "The current original event feed offer is unavailable.";

    internal static async Task<GrainOperationReply> ExecuteAsync(IEventVectorParent actualParent,
        DecodedGrainRequest original, EventFeedControlRequest request,
        global::Orleans.Runtime.IGrainContext context, ReadExecutionBudget work,
        IOptions<DatabaseLimits> limits, IOptions<GrainRoutingOptions> routing,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var token = writer.CancellationToken;
        token.ThrowIfCancellationRequested();
        limits.Value.Validate();
        if (request.Limit <= NoDeliveries || request.Limit > limits.Value.MaxResults)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidTail); }
        var stream = EventFeedTailStream.Resolve(context, request, work);
        using var observer = new EventFeedTailSignalOwner(request.MapId);
        StreamSubscriptionHandle<EventFeedWakeupHint>? subscription = null;
        GrainOperationReply? terminal = null;
        Exception? primary = null;
        Exception? cleanup = null;
        try
        {
            primary = await NativeRequestWorkSettlement.JoinAsync(OwnAsync()).ConfigureAwait(true);
        }
        finally
        {
            if (subscription is not null)
            { cleanup = await NativeRequestWorkSettlement.JoinAsync(subscription.UnsubscribeAsync()).ConfigureAwait(true); }
        }
        NativeRequestWorkSettlement.Rethrow(NativeRequestWorkSettlement.Preserve(primary, cleanup));
        return terminal ?? throw Errors.Fail(ErrorCode.NotFound, InvalidTail);

        async Task OwnAsync()
        {
            subscription = await stream.SubscribeAsync(observer).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            terminal = await ReadAsync(actualParent, original, request.Limit, observer,
                work, routing, writer).ConfigureAwait(true);
        }
    }

    private static async Task<EventFeedControlResult?> RecheckAsync(IEventVectorParent actualParent,
        DecodedGrainRequest original, EventFeedTailSignalOwner observer,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var before = observer.State().OriginalFailure;
        var read = new EventFeedTailReadAttempt(actualParent, original, work, cancellationToken);
        var readFailure = await NativeRequestWorkSettlement.JoinAsync(read.ReadAsync()).ConfigureAwait(true);
        var after = observer.State().OriginalFailure;
        var failure = before is null
            ? NativeRequestWorkSettlement.Preserve(readFailure, after)
            : NativeRequestWorkSettlement.Preserve(before, readFailure);
        if (before is null && after is not null)
        {
            var recheckFailure = await NativeRequestWorkSettlement.JoinAsync(read.ReadAsync()).ConfigureAwait(true);
            failure = NativeRequestWorkSettlement.Preserve(failure, recheckFailure);
        }
        NativeRequestWorkSettlement.Rethrow(failure);
        return read.Value;
    }

    private static async Task<GrainOperationReply> ReadAsync(IEventVectorParent actualParent,
        DecodedGrainRequest original, int maximum, EventFeedTailSignalOwner observer,
        ReadExecutionBudget work, IOptions<GrainRoutingOptions> routing,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        EventFeedControlResult? last = null;
        var delivered = NoDeliveries;
        while (delivered < maximum)
        {
            var signal = observer.CaptureSignal();
            var value = await RecheckAsync(actualParent, original, observer, work, writer.CancellationToken)
                .ConfigureAwait(true);
            var state = observer.State();
            if (state.OriginalFailure is not null)
            {
                _ = await RecheckAsync(actualParent, original, observer, work, writer.CancellationToken)
                    .ConfigureAwait(true);
            }
            if (value is not null && value.PublicCommandId != last?.PublicCommandId)
            {
                if (last is not null && value.Receipt.Token.Position <= last.Receipt.Token.Position)
                { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidTail); }
                work.CheckResult(value);
                await writer.ProgressAsync(new GrainRequestProgress(original.Envelope.RequestId)
                { EventFeedTail = new EventFeedTailProgress(Version, value) }).ConfigureAwait(true);
                last = value;
                delivered++;
            }
            if (delivered == maximum || state.Completed)
            { break; }
            await signal.WaitAsync(writer.CancellationToken).ConfigureAwait(true);
        }
        return last is null ? throw Errors.Fail(ErrorCode.NotFound, InvalidTail)
            : GrainReplyFactory.Value(last, routing, writer.CancellationToken);
    }
}
