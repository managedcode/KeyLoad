namespace KeyLoad.Server.Features.ClusterRouting;

[Flags]
internal enum PhysicalOwnerProbePredicate
{
    Unobserved = 0,
    StatusIncarnation = 1 << 0,
    StatusVoters = 1 << 1,
    StatusRoutingReady = 1 << 2,
    Fingerprint = 1 << 3,
    TransportReady = 1 << 4,
    LocalVoter = 1 << 5,
    DiscoveryIncarnation = 1 << 6,
    AppliedPosition = 1 << 7
}
