using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record DueNoQuorumRf3FaultPlan(
    string Leader,
    string RecoveryNode,
    string Survivor,
    NodeEpochRf3NodeObservation[] BeforeStatus,
    ReplicaSiloDiscovery[] BeforeDiscovery);
