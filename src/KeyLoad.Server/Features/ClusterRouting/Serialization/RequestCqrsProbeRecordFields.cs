namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeRecordFields
{
    private static readonly string[] OwnerFields = [nameof(RequestCqrsProbeOwnerRecord.Version), nameof(RequestCqrsProbeOwnerRecord.Kind), nameof(RequestCqrsProbeOwnerRecord.SessionId), nameof(RequestCqrsProbeOwnerRecord.Voter)];
    private static readonly string[] ArmFields = [nameof(RequestCqrsProbeArmRecord.Version), nameof(RequestCqrsProbeArmRecord.Kind), nameof(RequestCqrsProbeArmRecord.SessionId), nameof(RequestCqrsProbeArmRecord.ArmId), nameof(RequestCqrsProbeArmRecord.PrincipalId), nameof(RequestCqrsProbeArmRecord.CommandId), nameof(RequestCqrsProbeArmRecord.ReadKind), nameof(RequestCqrsProbeArmRecord.Phase), nameof(RequestCqrsProbeArmRecord.Action), nameof(RequestCqrsProbeArmRecord.Partition), nameof(RequestCqrsProbeArmRecord.SourceRequestId), nameof(RequestCqrsProbeArmRecord.TargetVoter), nameof(RequestCqrsProbeArmRecord.SourceArmId)];
    private static readonly string[] ReleaseFields = [nameof(RequestCqrsProbeReleaseRecord.Version), nameof(RequestCqrsProbeReleaseRecord.Kind), nameof(RequestCqrsProbeReleaseRecord.SessionId), nameof(RequestCqrsProbeReleaseRecord.ArmId), nameof(RequestCqrsProbeReleaseRecord.RequestId)];
    private static readonly string[] MarkerFields = [nameof(RequestCqrsProbeMarkerRecord.Version), nameof(RequestCqrsProbeMarkerRecord.Kind), nameof(RequestCqrsProbeMarkerRecord.SessionId), nameof(RequestCqrsProbeMarkerRecord.ArmId), nameof(RequestCqrsProbeMarkerRecord.RequestId), nameof(RequestCqrsProbeMarkerRecord.CommandId), nameof(RequestCqrsProbeMarkerRecord.Phase), nameof(RequestCqrsProbeMarkerRecord.Outcome), nameof(RequestCqrsProbeMarkerRecord.Voter), nameof(RequestCqrsProbeMarkerRecord.SiloAddress), nameof(RequestCqrsProbeMarkerRecord.EntryIndex), nameof(RequestCqrsProbeMarkerRecord.EntryTerm)];

    private static readonly string[] ActivationFields = [nameof(RequestCqrsProbeActivationRecord.Version), nameof(RequestCqrsProbeActivationRecord.Kind), nameof(RequestCqrsProbeActivationRecord.SessionId), nameof(RequestCqrsProbeActivationRecord.ArmId), nameof(RequestCqrsProbeActivationRecord.RequestId), nameof(RequestCqrsProbeActivationRecord.CommandId), nameof(RequestCqrsProbeActivationRecord.Voter), nameof(RequestCqrsProbeActivationRecord.SiloAddress), nameof(RequestCqrsProbeActivationRecord.GrainDigest), nameof(RequestCqrsProbeActivationRecord.ActivationId)];
    internal static ReadOnlySpan<string> Activation => ActivationFields;
    internal static ReadOnlySpan<string> Owner => OwnerFields;
    internal static ReadOnlySpan<string> Arm => ArmFields;
    internal static ReadOnlySpan<string> Release => ReleaseFields;
    internal static ReadOnlySpan<string> Marker => MarkerFields;
    private static readonly string[] LiveFields = [nameof(RequestCqrsProbeLiveRecord.Version), nameof(RequestCqrsProbeLiveRecord.Kind), nameof(RequestCqrsProbeLiveRecord.SessionId), nameof(RequestCqrsProbeLiveRecord.ArmId), nameof(RequestCqrsProbeLiveRecord.RequestId), nameof(RequestCqrsProbeLiveRecord.CommandId), nameof(RequestCqrsProbeLiveRecord.Voter), nameof(RequestCqrsProbeLiveRecord.SiloAddress), nameof(RequestCqrsProbeLiveRecord.GrainDigest), nameof(RequestCqrsProbeLiveRecord.ActivationId), nameof(RequestCqrsProbeLiveRecord.Step), nameof(RequestCqrsProbeLiveRecord.Nonce), nameof(RequestCqrsProbeLiveRecord.PredecessorSha256), nameof(RequestCqrsProbeLiveRecord.ChallengeSha256)];
    internal static ReadOnlySpan<string> Live => LiveFields;
}
