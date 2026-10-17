using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Transports the complete native request and proof in one generated Orleans reference graph.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(PartitionControlLifetimeTransportProtocol.Alias)]
internal sealed record PartitionControlLifetimeTransportV2(
    [property: Orleans.Id(PartitionControlLifetimeTransportProtocol.OriginalRequestId)] PartitionMovementTransportRequest OriginalRequest,
    [property: Orleans.Id(PartitionControlLifetimeTransportProtocol.ProofId)] PartitionMoveLifetimeProofV1 Proof)
{
    internal static PartitionControlLifetimeTransportV2 Create(PartitionMovementTransportRequest request,
        PartitionMoveLifetimeProofV1 proof)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(proof);
        if (request.Envelope.Grant is { } grant
            && PartitionControlLifetimeProofValidation.Same(proof.OriginalGrant, grant))
        {
            var shared = proof with
            {
                OriginalGrant = grant
            };
            if (!PartitionControlLifetimeProofValidation.Same(proof, shared))
            {
                throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid);
            }
            proof = shared;
        }
        return new(request, proof);
    }
}
