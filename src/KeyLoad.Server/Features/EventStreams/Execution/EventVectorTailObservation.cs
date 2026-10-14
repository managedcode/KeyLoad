using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorTailObservation
{
    private const int Version = 1;

    internal static async Task<EventFeedControlResult?> ReadAsync(RemoteDocumentRuntime runtime,
        OrleansNode node, PartitionHost partition, NodeOptions options, PhysicalOwnerDirectoryV1 directory,
        DecodedGrainRequest parent, EventFeedControlRequest request, string principalId,
        TimeProvider clock, EventVectorAdmissionPolicy admission, EventVectorRemoteEncoding encoding,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        EventVectorCurrentSourceRound? fresh = null;
        EventVectorOutgoingCall? call = null;
        EventVectorAuthenticatedExchange? exchange = null;
        EventFeedControlResult? result = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(ReadOriginalAsync, failures).ConfigureAwait(false);
        }
        finally
        {
            if (exchange is not null)
            {
                ServerFailureObserver.Observe(exchange.Dispose, failures);
            }
            if (call is not null)
            {
                ServerFailureObserver.Observe(call.Dispose, failures);
            }
            if (fresh is not null)
            {
                ServerFailureObserver.Observe(fresh.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;

        async Task ReadOriginalAsync()
        {
            fresh = await EventVectorCurrentSourceRound.CreateAsync(runtime, node, partition, options,
                directory, request, principalId, parent.Envelope.ExpiresAt, admission, encoding, work,
                cancellationToken).ConfigureAwait(false);
            var input = new EventVectorParentLookupCall(Version, principalId, request)
            {
                FreshSourceAuthorizationWitness = fresh.Witness,
                TailObservation = true
            };
            call = EventVectorOutgoingCall.Create(node, partition, options,
                EventVectorRemoteAction.ParentOutcome, input, parent.Envelope.ExpiresAt, work, encoding);
            exchange = await runtime.SendLocalEventVectorAsync(call.OriginalCall,
                work, cancellationToken).ConfigureAwait(false);
            _ = GrainRequestAuthority.ReloadForRequest(partition.Database, parent.Envelope, clock);
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
                result = Resolve(observed, partition, request, principalId, directory.ControlOwner, admission, work);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(observed.OriginalNativeControlPhaseBytes).Span);
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(observed.OriginalNativeOutcomeBytes).Span);
            }
        }
    }

    private static EventFeedControlResult? Resolve(EventVectorParentObservation observed,
        PartitionHost partition, EventFeedControlRequest request, string principalId,
        PhysicalShardRecord actualControl, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var identity = partition.Database.Store.Identity;
        if (observed.Version != Version || observed.PrincipalId != principalId
            || !PhysicalOwnerEntryValidation.SameOwner(observed.ControlOwner, actualControl)
            || observed.NodeId != identity.NodeId || observed.ReadGeneration != identity.ReadGeneration)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Invalid);
        }
        if (observed.OriginalNativeControlPhaseBytes.IsEmpty && observed.OriginalNativeOutcomeBytes.IsEmpty)
        {
            if (observed.OriginalPublicCommandId != request.CommandId)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid);
            }
            work.Check();
            return null;
        }
        if (observed.OriginalNativeControlPhaseBytes.IsEmpty || observed.OriginalNativeOutcomeBytes.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid);
        }
        var phase = NativeSerialization.Deserialize<EventVectorControlPhase>(observed.OriginalNativeControlPhaseBytes.Span);
        if (phase.Action != EventVectorControlAction.Offer || phase.OriginalRequest.MapId != request.MapId
            || phase.OriginalRequest.ControlPartition != request.ControlPartition
            || phase.OriginalRequest.CommandId != observed.OriginalPublicCommandId)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid);
        }
        var outcome = NativeSerialization.Deserialize<StoredOutcome>(observed.OriginalNativeOutcomeBytes.Span);
        if (outcome.Result.Error is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid);
        }
        var value = EventVectorOriginalPublicResult.Read(outcome.Result.Get<EventVectorControlResult>(), phase, admission)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid);
        work.CheckResult(value);
        return value;
    }
}
