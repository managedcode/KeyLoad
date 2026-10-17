using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Revalidates original raw native proofs while observing a fresh canonical control read.</summary>
internal static class PartitionMovementRetainedCaptureProofVerifier
{
    internal static void Require(DatabaseEngine database, PartitionMoveTransferReadAuthority authority,
        ReadExecutionBudget work, NodeOptions options)
    {
        var capture = authority.CapturePhase;
        var body = new PartitionMoveCheckpointBody(capture.Version, PartitionMoveCheckpointAction.Observe,
            authority.Header.OperatorPrincipalId, authority.Header.OriginalTransferRequest, authority.Header.Generation,
            capture.OriginalPhaseCommandId, null, null, capture.OriginalResult,
            capture.OriginalDescriptor, capture.OriginalFence, OriginalExpiresAt: capture.OriginalExpiresAt,
            OriginalCaptureWitness: capture.OriginalCaptureWitness, OriginalRequestNonce: capture.OriginalRequestNonce,
            OriginalOutcomeWitness: capture.OriginalOutcomeWitness, OriginalCaptureReleaseNonce: capture.OriginalCaptureReleaseNonce);
        PartitionMovementCaptureProofVerifier.Require(database, body, capture,
            capture.OriginalCaptureWitness!, work, options);
        PartitionMovementOutcomeProofVerifier.Require(database, body, capture,
            capture.OriginalOutcomeWitness!, work, options);
    }
}
