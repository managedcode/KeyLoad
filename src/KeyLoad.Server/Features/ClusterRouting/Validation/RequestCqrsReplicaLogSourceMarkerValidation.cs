namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsReplicaLogSourceMarkerValidation
{
    private const int SingleSourceMarker = 1;

    internal static void Require(RequestCqrsProbeSnapshot snapshot, RequestCqrsProbeArmRecord arm, string actualSiloAddress)
    {
        RequestCqrsCanonicalSourceArmValidation.Require(snapshot, arm);
        var count = snapshot.Markers.Count(marker => marker.ArmId == arm.SourceArmId
            && marker.SessionId == arm.SessionId && marker.CommandId == arm.CommandId
            && marker.RequestId == arm.SourceRequestId && marker.Voter == arm.TargetVoter
            && marker.SiloAddress == actualSiloAddress && marker.Phase == RequestCqrsProbePhase.BeforeSubmit
            && marker.Outcome == RequestCqrsProbeOutcome.Observed);
        if (count != SingleSourceMarker)
        { throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidFiles); }
    }
}
