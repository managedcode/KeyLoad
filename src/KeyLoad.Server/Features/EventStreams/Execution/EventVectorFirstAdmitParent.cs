using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorFirstAdmitParent(OrleansNode node, PartitionHost partition,
    RemoteDocumentRuntime runtime, IOptions<NodeOptions> options, IOptions<DatabaseLimits> limits,
    IOptions<GrainRoutingOptions> routing, TimeProvider clock) : IEventVectorParent
{
    private const int Version = 1;
    private const int FirstOrdinal = 0;

    public async Task<GrainOperationReply> ExecuteAdmitAsync(DecodedGrainRequest original,
        GrainRequestCodec codec, global::Orleans.Runtime.IGrainContext context, ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        var database = partition.Database;
        var principal = GrainRequestAuthority.ReloadForRequest(database, original.Envelope, clock);
        var request = GrainNativePayload.ReadCommand<EventFeedControlRequest>(original.Payload);
        var directory = database.ReadEventVectorConfiguredOwners(principal.Id, request,
            work, originalCancellationToken);
        var admission = new EventVectorAdmissionPolicy(limits);
        var encoding = new EventVectorRemoteEncoding(limits);
        var terminal = await EventVectorParentTerminalObservation.ReadAsync(runtime, node, partition,
            options.Value, request, principal.Id, directory, original.Envelope.ExpiresAt,
            admission, encoding, routing, work, originalCancellationToken).ConfigureAwait(false);
        if (terminal is not null)
        { return terminal; }
        if (request.Action == EventFeedControlAction.Offer)
        {
            return await EventVectorOfferParent.ExecuteAsync(runtime, node, partition, options,
                routing, clock, original, request, principal.Id, directory, admission, encoding,
                work, originalCancellationToken).ConfigureAwait(false);
        }
        if (request.Action == EventFeedControlAction.Refresh)
        {
            return await EventVectorRefreshParent.ExecuteAsync(runtime, node, partition, options,
                routing, clock, original, request, principal.Id, directory, admission, encoding,
                work, originalCancellationToken).ConfigureAwait(false);
        }
        if (request.Action is EventFeedControlAction.Acknowledge or EventFeedControlAction.Release
            && request.OriginalFirstIssuancePublicCommandId == Guid.Empty)
        {
            return await EventVectorSettlementParent.ExecuteAsync(runtime, node, partition, options,
                routing, clock, original, request, principal.Id, directory, admission, encoding,
                work, originalCancellationToken).ConfigureAwait(false);
        }
        if (request.Action == EventFeedControlAction.Release)
        {
            return await EventVectorFirstCleanupParent.ExecuteAsync(runtime, node, partition, options,
                routing, clock, original, request, principal.Id, directory, admission, encoding,
                work, originalCancellationToken).ConfigureAwait(false);
        }
        return await CaptureAndOpenAsync(original, codec, context, directory, principal, request, admission, encoding,
            work, originalCancellationToken).ConfigureAwait(false);
    }

    public Task<EventFeedControlResult?> ReadTailOfferAsync(DecodedGrainRequest original,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        var database = partition.Database;
        var principal = GrainRequestAuthority.ReloadForRequest(database, original.Envelope, clock);
        var request = GrainNativePayload.ReadCommand<EventFeedControlRequest>(original.Payload);
        if (request.Action != EventFeedControlAction.Tail)
        { throw Errors.Fail(ErrorCode.Validation, EventVectorRemoteProtocol.Invalid); }
        var directory = database.ReadEventVectorConfiguredOwners(principal.Id, request, work, originalCancellationToken);
        return EventVectorTailObservation.ReadAsync(runtime, node, partition, options.Value, directory,
            original, request, principal.Id, clock, new EventVectorAdmissionPolicy(limits),
            new EventVectorRemoteEncoding(limits), work, originalCancellationToken);
    }

    private async Task<GrainOperationReply> CaptureAndOpenAsync(DecodedGrainRequest original,
        GrainRequestCodec codec, global::Orleans.Runtime.IGrainContext context, PhysicalOwnerDirectoryV1 directory, PrincipalRecord principal, EventFeedControlRequest request,
        EventVectorAdmissionPolicy admission, EventVectorRemoteEncoding encoding,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        admission.RequireEntryCount(directory.Owners.Length);
        work.ChargeBytes(checked((long)directory.Owners.Length * IntPtr.Size));
        var captures = new EventVectorCapturedExchange[directory.Owners.Length];
        GrainOperationReply? result = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                for (var ordinal = FirstOrdinal; ordinal < captures.Length; ordinal++)
                {
                    captures[ordinal] = await EventVectorParentCapture.ExecuteAsync(runtime, node, partition,
                        options.Value, directory.Owners[ordinal], new(Version, request, principal.Id, ordinal),
                        original.Envelope.ExpiresAt, admission, encoding, work,
                        originalCancellationToken).ConfigureAwait(false);
                }
                await codec.ObservePhaseAsync(original, GrainRequestPhase.EventVectorFirstCapturesReady,
                    context, originalCancellationToken).ConfigureAwait(false);
                var issued = EventVectorFirstAdmitPreparation.Create(partition, clock, original,
                    captures, admission, encoding, work, originalCancellationToken);
                await codec.ObservePhaseAsync(original, GrainRequestPhase.EventVectorFirstAdmitPrepared,
                    context, originalCancellationToken).ConfigureAwait(false);
                var appended = await EventVectorIssuedAdmitCall.ExecuteAsync(node, original, principal, issued,
                    work, originalCancellationToken).ConfigureAwait(false);
                result = await EventVectorFreshOpenCompletion.ExecuteAsync(runtime, node, partition,
                    options, limits, routing, clock, original, codec, context, issued, appended, directory, principal.Id,
                    admission, encoding, work, originalCancellationToken).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            foreach (var capture in captures)
            { if (capture is not null) { ServerFailureObserver.Observe(capture.Dispose, failures); } }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
    }
}
