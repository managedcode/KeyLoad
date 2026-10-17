using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record DueFaultRf3LeaderCut(
    string LeaderNode,
    string[] Survivors,
    NodeEpochRf3NodeObservation[] BeforeStatus,
    ReplicaSiloDiscovery[] BeforeDiscovery,
    long RequiredApplied);
