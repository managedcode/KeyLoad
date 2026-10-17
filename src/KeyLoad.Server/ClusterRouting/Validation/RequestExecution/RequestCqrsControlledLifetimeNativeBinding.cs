using KeyLoad.Core.Features.ClusterRouting.Contracts;
using static KeyLoad.Server.Features.ClusterRouting.RequestCqrsControlledLifetimeProtocol;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Checks retained native shapes and identities; transport remains the sole MAC verifier.</summary>
internal static class RequestCqrsControlledLifetimeNativeBinding
{
    internal static PartitionMovementTransportRequest RequireCarrier(RequestCqrsControlledLifetimeStepWitness witness, byte[] bytes)
    {
        var carrier = NativeSerialization.Deserialize<PartitionControlLifetimeTransportV2>(bytes);
        var original = carrier.OriginalRequest;
        var delegation = carrier.Proof.ControlProof.OriginalDelegation;
        if (original.Envelope.Stage != PartitionMovePeerStage.ControlApplyCommand || original.Action != PartitionMovementTransportAction.Apply
            || original.CommandId != witness.EffectCommandId || delegation.EffectId != original.CommandId
            || carrier.Proof.OriginalGrant.PhaseCommandId != original.CommandId
            || delegation.Identity.CommandId != witness.CommandId || delegation.Identity.PrincipalId != witness.PrincipalId
            || original.Envelope.Partition != witness.Partition.ToPartition() || delegation.Identity.Partition != original.Envelope.Partition
            || original.CallerVoter != witness.CallerVoter || original.CallerSiloAddress != witness.CallerSiloAddress
            || original.Envelope.Nonce == Guid.Empty || witness.TechnicalPrincipalId != PartitionStoreProtocol.AdministratorId)
        {
            throw InvalidProof();
        }
        return original;
    }

    internal static (Guid OuterNonce, Guid InnerNonce) RequireNegative(RequestCqrsControlledLifetimeStepWitness witness,
        byte[] carrierBytes, byte[] requestBytes, byte[] replyBytes, byte[]? donorBytes)
    {
        var original = RequireCarrier(witness, carrierBytes);
        var request = NativeSerialization.Deserialize<PartitionMovementGrainServiceRequest>(requestBytes);
        var reply = NativeSerialization.Deserialize<PartitionMovementGrainServiceReply>(replyBytes);
        var missing = witness.Variation == ControlledLifetimeReplayVariation.Missing;
        var channel = missing ? PartitionMovementGrainServiceChannel.Phase : PartitionMovementGrainServiceChannel.ControlledLifetime;
        if (request.Channel != channel || request.Query || request.Nonce == Guid.Empty || reply.Nonce != request.Nonce
            || request.CallerVoter != witness.CallerVoter || request.CallerSiloAddress != witness.CallerSiloAddress
            || request.DestinationVoter != witness.ReceiverVoter || request.DestinationSiloAddress != witness.ReceiverSiloAddress
            || reply.Error != witness.Error || !reply.Payload.IsEmpty || !string.IsNullOrEmpty(reply.Signature)
            || reply.SafeDetail != PartitionMovementProtocol.Unavailable || string.IsNullOrEmpty(request.SenderSignature)
            || string.IsNullOrEmpty(reply.SenderSignature))
        {
            throw InvalidProof();
        }
        var carrier = missing ? null : NativeSerialization.Deserialize<PartitionControlLifetimeTransportV2>(request.Payload.Span);
        var changed = missing ? NativeSerialization.Deserialize<PartitionMovementTransportRequest>(request.Payload.Span) : carrier!.OriginalRequest;
        if (changed.Envelope.Nonce == Guid.Empty || changed.Envelope.Nonce == original.Envelope.Nonce
            || !NativeSerialization.Serialize(changed with
            {
                Envelope = changed.Envelope with
                {
                    Nonce = original.Envelope.Nonce
                }
            })
                .AsSpan().SequenceEqual(NativeSerialization.Serialize(NativeSerialization.Deserialize<PartitionControlLifetimeTransportV2>(carrierBytes).OriginalRequest)))
        {
            throw InvalidProof();
        }
        if (!missing)
        {
            var expected = witness.Variation == ControlledLifetimeReplayVariation.Substituted
                ? ControlledLifetimeReplayMutation.Substitute(NativeSerialization.Deserialize<PartitionControlLifetimeTransportV2>(carrierBytes).Proof)
                : NativeSerialization.Deserialize<PartitionControlLifetimeTransportV2>(donorBytes ?? throw InvalidProof()).Proof;
            if (!NativeSerialization.Serialize(expected).AsSpan().SequenceEqual(NativeSerialization.Serialize(carrier!.Proof)))
            {
                throw InvalidProof();
            }
        }
        return (request.Nonce, changed.Envelope.Nonce);
    }
    private static InvalidOperationException InvalidProof() => new(Invalid);
}
