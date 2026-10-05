using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Applies bounded marker assertions to retained claims, including after arm retirement.</summary>
internal static class RequestCqrsProbeMarkerState
{
    internal static void Record(string sessionId, RequestCqrsProbeArmState arm,
        RequestCqrsProbeMarkerRecord marker, string node, string expectedPhase, string expectedOutcome,
        IReadOnlyList<ReplicaSiloDiscovery> signedDiscovery, IDictionary<string, int> activeGates)
    {
        if (marker.Version != RequestCqrsProbeFixtureProtocol.Version
            || marker.Kind != RequestCqrsProbeFixtureProtocol.MarkerKind || marker.SessionId != sessionId
            || marker.RequestId == Guid.Empty || marker.CommandId != arm.CommandId
            || marker.Phase.ToString() != expectedPhase || marker.Outcome.ToString() != expectedOutcome
            || (arm.Retired && marker.Phase != RequestCqrsProbePhase.ProducerDisposed)
            || (marker.Phase != arm.Phase && marker.Phase != RequestCqrsProbePhase.ProducerDisposed)
            || ((marker.Outcome is RequestCqrsProbeOutcome.Released or RequestCqrsProbeOutcome.Cancelled)
                && arm.Action != RequestCqrsProbeAction.Hold)
            || (marker.Outcome == RequestCqrsProbeOutcome.FaultRequested
                && arm.Action != RequestCqrsProbeAction.ThrowOrdinary)
            || (marker.Phase == RequestCqrsProbePhase.ProducerDisposed
                && marker.Outcome != RequestCqrsProbeOutcome.Observed)
            || marker.Voter != RequestCqrsProbeFileNames.OriginForNode(node))
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
        if (arm.RequestId is { } claimed && claimed != marker.RequestId)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
        var matching = MatchDiscovery(marker, signedDiscovery);
        if (!matching.TransportReady || matching.SiloAddress != marker.SiloAddress)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
        if (!RememberExactMarker(arm, marker, node, activeGates))
        { return; }
        arm.RequestId = marker.RequestId;
        if (marker.Phase == RequestCqrsProbePhase.ProducerDisposed)
        { arm.ProducerDisposedSeen = true; }
        if (arm.Action == RequestCqrsProbeAction.Hold && marker.Phase == arm.Phase)
        { ApplyGateOutcome(arm, marker, node, activeGates); }
    }

    private static bool RememberExactMarker(RequestCqrsProbeArmState arm, RequestCqrsProbeMarkerRecord marker,
        string node, IDictionary<string, int> activeGates)
    {
        for (var index = 0; index < arm.MarkerRecordCount; index++)
        {
            var previous = arm.MarkerRecords[index]!;
            if (previous.ArmId == marker.ArmId && previous.RequestId == marker.RequestId
                && previous.Phase == marker.Phase && previous.Outcome == marker.Outcome && previous.Voter == marker.Voter)
            {
                if (!SameMarker(previous, marker))
                { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
                return false;
            }
        }
        ValidateGateTransition(arm, marker, node, activeGates);
        if (arm.MarkerRecordCount == arm.MarkerRecords.Length)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerLimitExceeded); }
        arm.MarkerRecords[arm.MarkerRecordCount++] = marker;
        return true;
    }

    private static void ValidateGateTransition(RequestCqrsProbeArmState arm, RequestCqrsProbeMarkerRecord marker,
        string node, IDictionary<string, int> activeGates)
    {
        if (arm.Action != RequestCqrsProbeAction.Hold || marker.Phase != arm.Phase)
        { return; }
        if (marker.Outcome == RequestCqrsProbeOutcome.Observed
            && (arm.GateVoter is not null || activeGates[node] >= RequestCqrsProbeFixtureProtocol.MaximumActiveGatesPerVoter))
        { throw new InvalidOperationException(arm.GateVoter is null
            ? RequestCqrsProbeFixtureProtocol.GateLimitExceeded : RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
        if ((marker.Outcome is RequestCqrsProbeOutcome.Released or RequestCqrsProbeOutcome.Cancelled)
            && (arm.Settled || arm.GateVoter != node))
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
    }

    private static bool SameMarker(RequestCqrsProbeMarkerRecord left, RequestCqrsProbeMarkerRecord right)
        => left.Version == right.Version && left.Kind == right.Kind && left.SessionId == right.SessionId
            && left.ArmId == right.ArmId && left.RequestId == right.RequestId && left.CommandId == right.CommandId
            && left.Phase == right.Phase && left.Outcome == right.Outcome && left.Voter == right.Voter
            && left.SiloAddress == right.SiloAddress;

    private static void ApplyGateOutcome(RequestCqrsProbeArmState arm, RequestCqrsProbeMarkerRecord marker,
        string node, IDictionary<string, int> activeGates)
    {
        if (marker.Outcome == RequestCqrsProbeOutcome.Observed)
        {
            if (arm.GateVoter is not null)
            { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
            if (activeGates[node] >= RequestCqrsProbeFixtureProtocol.MaximumActiveGatesPerVoter)
            { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.GateLimitExceeded); }
            arm.GateVoter = node;
            activeGates[node]++;
            return;
        }
        if (marker.Outcome is RequestCqrsProbeOutcome.Released or RequestCqrsProbeOutcome.Cancelled)
        {
            if (arm.Settled && arm.GateVoter == node)
            { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
            Settle(arm, node, activeGates);
        }
    }

    private static ReplicaSiloDiscovery MatchDiscovery(RequestCqrsProbeMarkerRecord marker,
        IReadOnlyList<ReplicaSiloDiscovery> discovery)
    {
        ReplicaSiloDiscovery? matching = null;
        foreach (var item in discovery)
        {
            if (item.VoterId == marker.Voter)
            {
                if (matching is not null)
                { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
                matching = item;
            }
        }
        return matching ?? throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch);
    }

    private static void Settle(RequestCqrsProbeArmState arm, string node, IDictionary<string, int> activeGates)
    {
        if (arm.Settled || arm.GateVoter != node)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
        arm.Settled = true;
        activeGates[node]--;
    }
}
