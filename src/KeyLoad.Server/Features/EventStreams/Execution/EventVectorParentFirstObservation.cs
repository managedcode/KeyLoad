using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorParentFirstObservation
{
    private const int Version = 1;

    internal static async Task<GrainOperationReply?> ReadAsync(RemoteDocumentRuntime runtime,
        OrleansNode node, PartitionHost partition, NodeOptions options,
        EventFeedControlRequest request, string principalId, PhysicalShardRecord actualControl,
        DateTimeOffset originalExpiry, EventVectorAdmissionPolicy admission,
        EventVectorRemoteEncoding encoding, IOptions<GrainRoutingOptions> routing,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var input = new EventVectorFirstLookupCall(Version, principalId,
            new(Version, request, actualControl));
        using var call = EventVectorOutgoingCall.Create(node, partition, options,
            EventVectorRemoteAction.FirstIssuanceOutcome, input, originalExpiry, work, encoding);
        using var exchange = await runtime.SendLocalEventVectorAsync(call.OriginalCall,
            work, cancellationToken).ConfigureAwait(false);
        var reply = exchange.Reply.Reply;
        if (reply.Error is { } error)
        { throw Errors.Fail(error, EventVectorRemoteProtocol.Unavailable); }
        admission.RequireEncodedBytes(reply.OriginalNativeReply.Length);
        work.ChargeBytes(reply.OriginalNativeReply.Length);
        var observed = NativeSerialization.Deserialize<EventVectorFirstIssuanceObservation>(reply.OriginalNativeReply.Span);
        try
        { return Resolve(observed, request, principalId, admission, routing, work, cancellationToken); }
        finally { Clear(observed); }
    }

    private static GrainOperationReply? Resolve(EventVectorFirstIssuanceObservation observed,
        EventFeedControlRequest request, string principalId, EventVectorAdmissionPolicy admission,
        IOptions<GrainRoutingOptions> routing, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var identity = EventVectorFirstIssuanceIds.Admit(request, principalId, admission);
        if (observed.OriginalAdmitCommandId != EventVectorFirstIssuanceIds.For(identity, admission))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid); }
        if (!observed.OriginalAdmitStoredOutcomeBytes.IsEmpty)
        {
            var original = NativeSerialization.Deserialize<StoredOutcome>(observed.OriginalAdmitStoredOutcomeBytes.Span);
            if (original.Result.Error is null)
            {
                throw Errors.Fail(ErrorCode.UnknownWriteOutcome, EventVectorRemoteProtocol.Unavailable);
            }
            return GrainReplyFactory.Operation(original.Result, routing, cancellationToken);
        }
        if (!observed.ActiveIssuanceRowBytes.IsEmpty || !observed.CurrentMapRowBytes.IsEmpty
            || !observed.OriginalParentCallRowBytes.IsEmpty || !observed.OriginalParentOutcomeIndexRowBytes.IsEmpty
            || !observed.AbandonedChargedIssuanceRowBytes.IsEmpty || !observed.TerminalIssuanceRowBytes.IsEmpty
            || !observed.OriginalFirstOpenStoredOutcomeBytes.IsEmpty)
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, EventVectorRemoteProtocol.Unavailable); }
        work.Check();
        return null;
    }

    private static void Clear(EventVectorFirstIssuanceObservation observed)
    {
        Zero(observed.OriginalAdmitStoredOutcomeBytes);
        Zero(observed.ActiveIssuanceRowBytes);
        Zero(observed.CurrentMapRowBytes);
        Zero(observed.OriginalParentCallRowBytes);
        Zero(observed.OriginalParentOutcomeIndexRowBytes);
        Zero(observed.AbandonedChargedIssuanceRowBytes);
        Zero(observed.TerminalIssuanceRowBytes);
        Zero(observed.OriginalFirstOpenStoredOutcomeBytes);
        Zero(observed.ActualControlCapacityRowBytes);
    }

    private static void Zero(ReadOnlyMemory<byte> bytes)
        => CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(bytes).Span);
}
