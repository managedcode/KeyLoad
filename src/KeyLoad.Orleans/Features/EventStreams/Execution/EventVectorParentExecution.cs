using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class EventVectorParentExecution
{
    private const int AdvisoryVersion = 1;
    internal static async Task<GrainOperationReply> ExecuteAdmitAsync(DecodedGrainRequest original,
        IServiceProvider services, TimeProvider clock, GrainRequestCodec codec, global::Orleans.Runtime.IGrainContext context,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var token = writer.CancellationToken;
        token.ThrowIfCancellationRequested();
        if (original.Envelope.Purpose != EventVectorRequestScope.ParentPurpose
            || original.Envelope.CommandKind != OperationKind.EventFeedControl
            || original.Envelope.ReadKind is not null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.InvalidRequest); }
        var database = services.GetRequiredService<DatabaseEngine>();
        _ = GrainRequestAuthority.ReloadForRequest(database, original.Envelope, clock);
        var parent = services.GetService<IEventVectorParent>()
            ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest);
        var work = new ReadExecutionBudget(services.GetRequiredService<IOptions<DatabaseLimits>>(), clock, token);
        var request = GrainNativePayload.ReadCommand<EventFeedControlRequest>(original.Payload);
        var limits = services.GetRequiredService<IOptions<DatabaseLimits>>();
        var routing = services.GetRequiredService<IOptions<GrainRoutingOptions>>();
        if (request.Action == EventFeedControlAction.Tail)
        {
            return await EventFeedTailExecution.ExecuteAsync(parent, original, request, context,
                work, limits, routing, writer).ConfigureAwait(true);
        }
        var result = await parent.ExecuteAdmitAsync(original, codec, context, work, token).ConfigureAwait(true);
        if (result.Error is null)
        {
            work.ChargeBytes(result.Payload.Length);
            var value = NativeSerialization.Deserialize<GrainValue>(result.Payload.Span).Value as EventFeedControlResult;
            if (value is not null && value.MapId == request.MapId)
            {
                var stream = EventFeedTailStream.Resolve(context, request, work);
                await new EventFeedAdvisoryPublisher(limits).PublishAsync(stream,
                    new EventFeedWakeupHint(AdvisoryVersion, value.MapId, value.Revision, value.CoverageGeneration),
                    token).ConfigureAwait(true);
            }
        }
        return result;
    }
}
