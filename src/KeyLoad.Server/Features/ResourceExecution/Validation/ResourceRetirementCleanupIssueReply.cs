using KeyLoad.Clustering;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementCleanupIssueReply
{
    internal static void Require(ResourceRetirementReadRequestV1 query, ResourceRetirementReadReplyV1 reply, DatabaseLimits limits)
    {
        var operation = reply.OriginalOperation;
        var empty = reply with
        {
            OriginalOperation = null,
            ReadBytes = ResourceRetirementReadContract.NoReadBytes,
            ExaminedRecords = ResourceRetirementReadContract.NoRecords
        };
        if (operation is null || !ResourceRetirementValidation.Same(empty, new ResourceRetirementReadReplyV1(null, null, null,
                ResourceRetirementReadContract.NoReadBytes, ResourceRetirementReadContract.NoRecords))
            || operation.Kind != OperationKind.ResourceRetirementPhase || operation.Id != query.Attempt!.OriginalCommandId
            || operation.NativePayload.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
        }
        var phase = NativeCommandPayload.Read<ResourceRetirementPhaseCommandV1>(operation);
        if (phase.Phase != ResourceRetirementPhase.CleanupPage || phase.RetirementId != query.Request!.RetirementId
            || !ResourceRetirementValidation.Same(NativeSerialization.Deserialize<ResourceRetirementCleanupPageBodyV1>(phase.Body.Span), query.CleanupPage))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
        }
        ResourceRetirementValidation.Bound(reply, limits);
    }
}
