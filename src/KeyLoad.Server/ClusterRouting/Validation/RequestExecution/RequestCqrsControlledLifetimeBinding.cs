using static KeyLoad.Server.Features.ClusterRouting.RequestCqrsControlledLifetimeProtocol;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsControlledLifetimeBinding
{
    internal static void RequireSame(RequestCqrsControlledLifetimeStepWitness baseline, RequestCqrsControlledLifetimeStepWitness current)
    {
        if (baseline.CommandId != current.CommandId || baseline.ConnectionId != current.ConnectionId
            || baseline.PrincipalId != current.PrincipalId || baseline.Partition != current.Partition
            || baseline.EffectCommandId != current.EffectCommandId || baseline.TechnicalPrincipalId != current.TechnicalPrincipalId
            || baseline.CallerVoter != current.CallerVoter || baseline.CallerSiloAddress != current.CallerSiloAddress
            || baseline.ReceiverVoter != current.ReceiverVoter || baseline.ReceiverSiloAddress != current.ReceiverSiloAddress
            || baseline.CarrierBytes != current.CarrierBytes || baseline.CarrierSha256 != current.CarrierSha256
            || baseline.CarrierParts != current.CarrierParts || baseline.SourceArmId != current.SourceArmId
            || current.Variation != ControlledLifetimeReplayMutation.At(current.Step)
            || current.Error != ControlledLifetimeReplayMutation.Expected(current.Variation))
        {
            throw new InvalidOperationException(Invalid);
        }
    }
    internal static int PartCount(RequestCqrsControlledLifetimeStepWitness value, string role) => role switch
    {
        CarrierRole => value.CarrierParts,
        RequestRole => value.RequestParts,
        ReplyRole => value.ReplyParts,
        _ => throw new InvalidOperationException(Invalid)
    };
}
