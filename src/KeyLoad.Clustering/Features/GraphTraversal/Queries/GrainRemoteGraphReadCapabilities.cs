using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.GraphTraversal;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

internal static class GrainRemoteGraphReadCapabilities
{
    internal static object Execute(DatabaseEngine database, IServiceProvider services, TimeProvider clock,
        PrincipalRecord principal, DecodedGrainRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(clock);
        token.ThrowIfCancellationRequested();
        if (!RemoteGraphRequestScope.HandlesRead(request.Envelope.ReadKind)
            || request.Envelope.Purpose != RemoteGraphRequestScope.ReadPurpose)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        var owner = services.GetRequiredService<PhysicalShardRecord>();
        var work = database.CreateRemoteGraphReadWork(request.Envelope.ExpiresAt, token);
        return request.Envelope.ReadKind switch
        {
            GrainReadKind.RemoteGraphTargetPreparation => Preparation(database, principal, request, owner, work),
            GrainReadKind.RemoteGraphSourceIntent => Source(database, principal, request, owner, work),
            GrainReadKind.RemoteGraphReceiverReceipt => Receipt(database, principal, request, owner, work),
            GrainReadKind.RemoteGraphIncoming => Incoming(database, principal, request, owner, work),
            GrainReadKind.RemoteGraphReceiverCandidates => database.ReadRemoteGraphReceiverCandidates(principal.Id,
                GrainNativePayload.Read<RemoteGraphReceiverCandidatesRequestV1>(request.Payload), work),
            GrainReadKind.RemoteGraphIncomingFinalize => Finalize(database, principal, request, owner, work),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    private static RemoteGraphTargetPreparationV1 Preparation(DatabaseEngine database, PrincipalRecord principal,
        DecodedGrainRequest request, PhysicalShardRecord owner, ReadExecutionBudget work)
    {
        var input = GrainNativePayload.Read<RemoteGraphPreparationRequestV1>(request.Payload);
        RequireOwner(input.DestinationOwner?.Owner, owner);
        return database.ReadRemoteGraphTargetPreparation(principal.Id, input, work);
    }

    private static RemoteGraphSourceSnapshotV1 Source(DatabaseEngine database, PrincipalRecord principal,
        DecodedGrainRequest request, PhysicalShardRecord owner, ReadExecutionBudget work)
    {
        var input = GrainNativePayload.Read<RemoteGraphSourceReadRequestV1>(request.Payload);
        RequireOwner(input.SourceOwner, owner);
        return database.ReadRemoteGraphSourceState(principal.Id, input, work);
    }

    private static RemoteGraphReceiverResultV1 Receipt(DatabaseEngine database, PrincipalRecord principal,
        DecodedGrainRequest request, PhysicalShardRecord owner, ReadExecutionBudget work)
    {
        var input = GrainNativePayload.Read<RemoteGraphReceiverReceiptReadRequestV1>(request.Payload);
        RequireOwner(input.Intent?.DestinationOwner?.Owner, owner);
        return database.ReadRemoteGraphReceiverReceipt(principal.Id, input, work);
    }

    private static RemoteGraphIncomingSourceProofV1 Incoming(DatabaseEngine database, PrincipalRecord principal,
        DecodedGrainRequest request, PhysicalShardRecord owner, ReadExecutionBudget work)
    {
        var input = GrainNativePayload.Read<RemoteGraphIncomingSourceProofRequestV1>(request.Payload);
        RequireOwner(input.Intent?.SourceOwner, owner);
        return database.ReadRemoteGraphIncomingSourceProof(principal.Id, input, work);
    }

    private static RemoteIncomingGraphEdgesPageV1 Finalize(DatabaseEngine database, PrincipalRecord principal,
        DecodedGrainRequest request, PhysicalShardRecord owner, ReadExecutionBudget work)
    {
        var input = GrainNativePayload.Read<RemoteGraphIncomingFinalizeRequestV1>(request.Payload);
        RequireOwner(input.Candidates?.TargetCut?.Owner, owner);
        return database.ReadRemoteGraphIncomingFinalize(principal.Id, input, work);
    }

    private static void RequireOwner(PhysicalShardRecord? supplied, PhysicalShardRecord actual)
    {
        if (supplied is null || !PhysicalOwnerEntryValidation.SameOwner(supplied, actual))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
