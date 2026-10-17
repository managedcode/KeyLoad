using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorParentTerminalObservation
{
    private const int Version = 1;

    internal static async Task<GrainOperationReply?> ReadAsync(RemoteDocumentRuntime runtime,
        OrleansNode node, PartitionHost partition, NodeOptions options,
        EventFeedControlRequest request, string principalId, PhysicalOwnerDirectoryV1 directory,
        DateTimeOffset originalExpiry, EventVectorAdmissionPolicy admission,
        EventVectorRemoteEncoding encoding, IOptions<GrainRoutingOptions> routing,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        EventVectorCurrentSourceRound? fresh = null;
        GrainOperationReply? result = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                fresh = await EventVectorCurrentSourceRound.CreateAsync(runtime, node, partition, options,
                    directory, request, principalId, originalExpiry, admission, encoding, work, cancellationToken).ConfigureAwait(false);
                result = await ReadOwnedAsync(runtime, node, partition, options, directory, request, principalId,
                    originalExpiry, fresh.Witness, admission, encoding, routing, work, cancellationToken).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally { if (fresh is not null) { ServerFailureObserver.Observe(fresh.Dispose, failures); } }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    private static async Task<GrainOperationReply?> ReadOwnedAsync(RemoteDocumentRuntime runtime,
        OrleansNode node, PartitionHost partition, NodeOptions options, PhysicalOwnerDirectoryV1 directory,
        EventFeedControlRequest request, string principalId, DateTimeOffset originalExpiry,
        ReadOnlyMemory<byte> witness, EventVectorAdmissionPolicy admission, EventVectorRemoteEncoding encoding,
        IOptions<GrainRoutingOptions> routing, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var input = new EventVectorParentLookupCall(Version, principalId, request)
        {
            FreshSourceAuthorizationWitness = witness
        };
        using var call = EventVectorOutgoingCall.Create(node, partition, options,
            EventVectorRemoteAction.ParentOutcome, input, originalExpiry, work, encoding);
        using var exchange = await runtime.SendLocalEventVectorAsync(call.OriginalCall,
            work, cancellationToken).ConfigureAwait(false);
        var reply = exchange.Reply.Reply;
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, EventVectorRemoteProtocol.Unavailable);
        }
        admission.RequireEncodedBytes(reply.OriginalNativeReply.Length);
        work.ChargeBytes(reply.OriginalNativeReply.Length);
        var observed = NativeSerialization.Deserialize<EventVectorParentObservation>(reply.OriginalNativeReply.Span);
        try
        {
            return Resolve(observed, partition, request, principalId, directory.ControlOwner,
                admission, routing, work, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(observed.OriginalNativeControlPhaseBytes).Span);
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(observed.OriginalNativeOutcomeBytes).Span);
        }
    }

    private static GrainOperationReply? Resolve(EventVectorParentObservation observed,
        PartitionHost partition, EventFeedControlRequest request, string principalId,
        PhysicalShardRecord actualControl, EventVectorAdmissionPolicy admission,
        IOptions<GrainRoutingOptions> routing, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var identity = partition.Database.Store.Identity;
        if (observed.Version != Version || observed.PrincipalId != principalId
            || observed.OriginalPublicCommandId != request.CommandId || !PhysicalOwnerEntryValidation.SameOwner(observed.ControlOwner, actualControl)
            || observed.NodeId != identity.NodeId || observed.ReadGeneration != identity.ReadGeneration)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Invalid);
        }
        if (observed.OriginalNativeControlPhaseBytes.IsEmpty && observed.OriginalNativeOutcomeBytes.IsEmpty)
        {
            work.Check();
            return null;
        }
        if (observed.OriginalNativeControlPhaseBytes.IsEmpty || observed.OriginalNativeOutcomeBytes.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid);
        }
        var phase = NativeSerialization.Deserialize<EventVectorControlPhase>(observed.OriginalNativeControlPhaseBytes.Span);
        var outcome = NativeSerialization.Deserialize<StoredOutcome>(observed.OriginalNativeOutcomeBytes.Span);
        if (outcome.Result.Error is not null)
        {
            return GrainReplyFactory.Operation(outcome.Result, routing, cancellationToken);
        }
        var value = EventVectorFirstCleanupResultValidation.Handles(phase.Action)
            ? EventVectorFirstCleanupResultValidation.Read(outcome.Result.Get<EventVectorFirstIssuanceResult>(),
                phase, principalId, identity.Incarnation, admission)
            : EventVectorOriginalPublicResult.Read(outcome.Result.Get<EventVectorControlResult>(), phase, admission)
                ?? throw Errors.Fail(ErrorCode.UnknownWriteOutcome, EventVectorRemoteProtocol.Unavailable);
        var measured = NativeSerialization.Measure(new GrainValue(value));
        admission.RequireEncodedBytes(measured);
        work.ChargeBytes(measured);
        work.Check();
        return GrainReplyFactory.Value(value, routing, cancellationToken);
    }
}
