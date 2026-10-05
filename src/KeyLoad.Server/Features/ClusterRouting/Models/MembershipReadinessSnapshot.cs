namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record MembershipReadinessSnapshot(int Version, int ActiveSilos, int MembershipRows,
    string ActiveFingerprint);
