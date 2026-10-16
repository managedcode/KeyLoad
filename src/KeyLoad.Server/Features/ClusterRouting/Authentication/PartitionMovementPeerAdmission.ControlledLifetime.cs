using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementPeerAdmission
{
    /// <summary>Authenticates the new carrier before sharing the original configured-owner admission path.</summary>
    internal Task<(PartitionMovementTransportRequest Request, PartitionMoveLifetimeProofV1 Proof)>
        VerifyControlledLifetimeAsync(ReadOnlyMemory<byte> originalBody, string signature, CancellationToken cancellationToken)
        => PartitionMovementPeerFrameAdmission.VerifyControlledLifetimeAsync(this, originalBody, signature, cancellationToken);
}
