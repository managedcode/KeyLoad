using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record DueNoQuorumRf3FaultPlan(
    string Leader,
    string RecoveryNode,
    string Survivor,
    NodeEpochRf3NodeObservation[] BeforeStatus,
    ReplicaSiloDiscovery[] BeforeDiscovery);
