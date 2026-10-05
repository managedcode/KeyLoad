namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeRecordFields
{
    private static readonly string[] OwnerFields = ["Version", "Kind", "SessionId", "Voter"];
    private static readonly string[] ArmFields = ["Version", "Kind", "SessionId", "ArmId", "PrincipalId", "CommandId", "ReadKind", "Phase", "Action"];
    private static readonly string[] ReleaseFields = ["Version", "Kind", "SessionId", "ArmId", "RequestId"];
    private static readonly string[] MarkerFields = ["Version", "Kind", "SessionId", "ArmId", "RequestId", "CommandId", "Phase", "Outcome", "Voter", "SiloAddress"];
    private static readonly string[] DiscoveryFields = ["Version", "Kind", "SessionId", "ObserverVoterId", "PeerVoterId", "ApplicationRpcVersion", "PeerEnvelopeVersion", "TransportReady", "ProtocolCompatible"];

    internal static ReadOnlySpan<string> Owner => OwnerFields;
    internal static ReadOnlySpan<string> Arm => ArmFields;
    internal static ReadOnlySpan<string> Release => ReleaseFields;
    internal static ReadOnlySpan<string> Marker => MarkerFields;
    internal static ReadOnlySpan<string> Discovery => DiscoveryFields;
}
