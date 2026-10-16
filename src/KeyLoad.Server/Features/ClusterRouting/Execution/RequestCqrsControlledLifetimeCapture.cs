using static KeyLoad.Server.Features.ClusterRouting.RequestCqrsControlledLifetimeProtocol;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsControlledLifetimeCapture
{
    internal static RequestCqrsControlledLifetimeStepWitness Baseline(RequestCqrsProbeClaim claim,
        RequestCqrsControlledLifetimePlan plan, PartitionMovementTransportRequest original,
        ControlledLifetimeReplayExchange owner, byte[] carrier, int maximumPartBytes)
    {
        var transport = NativeSerialization.Deserialize<PartitionControlLifetimeTransportV1>(carrier);
        var delegation = transport.Proof.ControlProof.OriginalDelegation;
        if (!NativeSerialization.Serialize(original).AsSpan().SequenceEqual(transport.OriginalRequest.Span)
            || delegation.Identity.CommandId != claim.Identity.CommandId || delegation.Identity.PrincipalId != claim.Identity.PrincipalId
            || delegation.EffectId != original.CommandId || transport.Proof.OriginalGrant.PhaseCommandId != original.CommandId
            || delegation.Identity.Partition != original.Envelope.Partition || claim.Arm.Record.Partition?.ToPartition() != original.Envelope.Partition)
        {
            throw new InvalidOperationException(Invalid);
        }
        var partition = original.Envelope.Partition;
        return new(RequestCqrsProbeProtocol.Version, StepKind, plan.SessionId, plan.ArmId, claim.Identity.RequestId,
            plan.CommandId, claim.ConnectionId, BaselineStep, ControlledLifetimeReplayVariation.CaptureOriginal, plan.PrincipalId,
            new(partition.TenantId, partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey), original.CommandId,
            PartitionStoreProtocol.AdministratorId, original.CallerVoter, original.CallerSiloAddress, owner.ReceiverVoter,
            owner.ReceiverSiloAddress, carrier.Length, RequestCqrsControlledLifetimeParts.Digest(carrier),
            RequestCqrsControlledLifetimeParts.Count(carrier.Length, maximumPartBytes), EmptyBytes, EmptyDigest, EmptyBytes,
            EmptyBytes, EmptyDigest, EmptyBytes, null, plan.SourceArmId);
    }

    internal static (RequestCqrsControlledLifetimeStepWitness Witness, byte[] Request, byte[] Reply) Negative(
        RequestCqrsControlledLifetimeStepWitness baseline, int step, ControlledLifetimeReplayResult actual, int maximumPartBytes)
    {
        var request = NativeSerialization.Serialize(actual.Request);
        var reply = NativeSerialization.Serialize(actual.Reply);
        var variation = ControlledLifetimeReplayMutation.At(step);
        if (actual.Reply.Error != ControlledLifetimeReplayMutation.Expected(variation))
        {
            throw new InvalidOperationException(Invalid);
        }
        return (baseline with
        {
            Step = step,
            Variation = variation,
            Error = actual.Reply.Error,
            RequestBytes = request.Length,
            RequestSha256 = RequestCqrsControlledLifetimeParts.Digest(request),
            RequestParts = RequestCqrsControlledLifetimeParts.Count(request.Length, maximumPartBytes),
            ReplyBytes = reply.Length,
            ReplySha256 = RequestCqrsControlledLifetimeParts.Digest(reply),
            ReplyParts = RequestCqrsControlledLifetimeParts.Count(reply.Length, maximumPartBytes)
        }, request, reply);
    }
}
