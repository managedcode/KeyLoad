using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsProbeFileNames
{
    internal static string OriginForNode(string node) => node switch
    {
        RequestCqrsRf3Protocol.Node1 => RequestCqrsProbeFixtureProtocol.Node1Origin,
        RequestCqrsRf3Protocol.Node2 => RequestCqrsProbeFixtureProtocol.Node2Origin,
        RequestCqrsRf3Protocol.Node3 => RequestCqrsProbeFixtureProtocol.Node3Origin,
        _ => throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry)
    };

    internal static bool IsMarkerName(string name)
    {
        var parts = name[..^5].Split('-');
        if (parts.Length != 5 || parts[0] != "marker"
            || !Guid.TryParseExact(parts[1], "N", out var armId) || armId.ToString("N") != parts[1]
            || !Guid.TryParseExact(parts[2], "N", out var requestId) || requestId.ToString("N") != parts[2]
            || !Enum.TryParse<RequestCqrsProbePhase>(parts[3], false, out var phase) || !Enum.IsDefined(phase)
            || phase.ToString() != parts[3]
            || !Enum.TryParse<RequestCqrsProbeOutcome>(parts[4], false, out var outcome) || !Enum.IsDefined(outcome))
        { return false; }
        return outcome.ToString() == parts[4];
    }

    internal static string Arm(Guid armId) => RequestCqrsProbeFixtureProtocol.ArmFilePrefix + armId.ToString("N") + ".json";
    internal static string Release(Guid armId, Guid requestId)
        => RequestCqrsProbeFixtureProtocol.ReleaseFilePrefix + armId.ToString("N") + "-" + requestId.ToString("N") + ".json";
    internal static string Marker(RequestCqrsProbeMarkerRecord marker)
        => RequestCqrsProbeFixtureProtocol.MarkerFilePrefix + marker.ArmId.ToString("N") + "-" + marker.RequestId.ToString("N") + "-"
            + marker.Phase + "-" + marker.Outcome + ".json";
}
