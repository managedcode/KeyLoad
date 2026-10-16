using KeyLoad.Clustering;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementSourceSealReply
{
    internal static void Require(ResourceRetirementReadRequestV1 query,
        ResourceRetirementReadReplyV1 reply, DatabaseLimits limits)
    {
        if (reply.OriginalOperation is not { } original || query.SourceSeal is not { } body
            || reply.State is not null || reply.Fence is not null || reply.InventoryControl is not null
            || reply.Attempt is not null || reply.PreparedOperation is not null || reply.Preparation is not null
            || reply.OwnerStatus is not null || reply.WorkJoinWitness is not null || reply.AcknowledgedOwner is not null
            || reply.Parent is not null || reply.AdmittedDrop is not null || original.Id != query.Attempt!.OriginalCommandId
            || original.Kind != OperationKind.ResourceRetirementPhase || original.PrincipalId != query.Attempt.PrincipalId)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
        }
        var phase = NativeCommandPayload.Read<ResourceRetirementPhaseCommandV1>(original);
        if (phase.Phase != ResourceRetirementPhase.SealBarrier || phase.RetirementId != query.Request!.RetirementId
            || !ResourceRetirementValidation.Same(NativeSerialization.Deserialize<ResourceRetirementSealBodyV1>(phase.Body.Span), body))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
        }
        ResourceRetirementValidation.Bound(reply, limits);
    }
}
