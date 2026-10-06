namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeRecordFields
{
    private static readonly string[] OwnerFields = [nameof(RequestCqrsProbeOwnerRecord.Version), nameof(RequestCqrsProbeOwnerRecord.Kind), nameof(RequestCqrsProbeOwnerRecord.SessionId), nameof(RequestCqrsProbeOwnerRecord.Voter)];
    private static readonly string[] ArmFields = [nameof(RequestCqrsProbeArmRecord.Version), nameof(RequestCqrsProbeArmRecord.Kind), nameof(RequestCqrsProbeArmRecord.SessionId), nameof(RequestCqrsProbeArmRecord.ArmId), nameof(RequestCqrsProbeArmRecord.PrincipalId), nameof(RequestCqrsProbeArmRecord.CommandId), nameof(RequestCqrsProbeArmRecord.ReadKind), nameof(RequestCqrsProbeArmRecord.Phase), nameof(RequestCqrsProbeArmRecord.Action)];
    private static readonly string[] ReleaseFields = [nameof(RequestCqrsProbeReleaseRecord.Version), nameof(RequestCqrsProbeReleaseRecord.Kind), nameof(RequestCqrsProbeReleaseRecord.SessionId), nameof(RequestCqrsProbeReleaseRecord.ArmId), nameof(RequestCqrsProbeReleaseRecord.RequestId)];
    private static readonly string[] MarkerFields = [nameof(RequestCqrsProbeMarkerRecord.Version), nameof(RequestCqrsProbeMarkerRecord.Kind), nameof(RequestCqrsProbeMarkerRecord.SessionId), nameof(RequestCqrsProbeMarkerRecord.ArmId), nameof(RequestCqrsProbeMarkerRecord.RequestId), nameof(RequestCqrsProbeMarkerRecord.CommandId), nameof(RequestCqrsProbeMarkerRecord.Phase), nameof(RequestCqrsProbeMarkerRecord.Outcome), nameof(RequestCqrsProbeMarkerRecord.Voter), nameof(RequestCqrsProbeMarkerRecord.SiloAddress)];

    internal static ReadOnlySpan<string> Owner => OwnerFields;
    internal static ReadOnlySpan<string> Arm => ArmFields;
    internal static ReadOnlySpan<string> Release => ReleaseFields;
    internal static ReadOnlySpan<string> Marker => MarkerFields;
}
