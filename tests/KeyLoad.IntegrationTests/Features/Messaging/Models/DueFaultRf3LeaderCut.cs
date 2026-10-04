using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record DueFaultRf3LeaderCut(
    string LeaderNode,
    string[] Survivors,
    NodeEpochRf3NodeObservation[] BeforeStatus,
    ReplicaSiloDiscovery[] BeforeDiscovery,
    long RequiredApplied);
