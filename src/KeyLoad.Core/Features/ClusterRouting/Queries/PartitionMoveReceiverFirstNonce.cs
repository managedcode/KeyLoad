using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private Guid ReadMoveReceiverFirstNonce(string principalId, Guid originalId,
        PartitionMovePeerEnvelope envelope, ReadExecutionBudget work)
    {
        movementCheckpointVerifier.RequireReceiverAdministrator(this, principalId, work);
        return Store.Read(view =>
        {
            var bounded = work.CreateView(view);
            var principal = RequireMoveDispatchPrincipal(bounded, principalId, EvaluationClock.GetUtcNow());
            var issued = PartitionMoveReceiverIssuanceStorage.Read(bounded, envelope.Partition,
                envelope.MoveId, originalId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            var original = envelope with
            {
                Nonce = issued.OriginalRequestNonce,
                ReceiverIssuanceProof = null,
                SourceDispatchWitness = null
            };
            var originalEpoch = RequireMoveReceiverOriginalObservationEpoch(bounded, principal, original, originalId);
            if (principal.PolicyEpoch != originalEpoch)
            { throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage); }
            work.Check();
            return issued.OriginalRequestNonce;
        });
    }
}
