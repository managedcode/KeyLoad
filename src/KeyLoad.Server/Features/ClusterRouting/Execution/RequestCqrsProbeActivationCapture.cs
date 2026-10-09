using System.Security.Cryptography;
using System.Text;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeActivationCapture
{
    internal static void Observe(RequestCqrsProbeFiles files, RequestCqrsProbeMarkerRecord marker, IGrainContext context)
    {
        if (marker.Phase != RequestCqrsProbePhase.BeforeSubmit || marker.Outcome != RequestCqrsProbeOutcome.Observed
            || context.GrainInstance is not CommandPartitionGrain)
        { return; }
        files.WriteActivation(Create(marker, context));
    }

    internal static RequestCqrsProbeActivationRecord Create(RequestCqrsProbeMarkerRecord marker, IGrainContext context)
    {
        if (context.ActivationId.IsDefault)
        { throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidRecord); }
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(context.GrainId.ToString())));
        return new(marker.Version, RequestCqrsProbeActivationProtocol.Kind, marker.SessionId,
            marker.ArmId, marker.RequestId, marker.CommandId, marker.Voter, marker.SiloAddress,
            digest, context.ActivationId.ToParsableString());
    }
}
