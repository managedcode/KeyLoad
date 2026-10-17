using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record DueFaultRf3RestartState(
    NodeEpochRf3NodeObservation[] Status,
    ReplicaSiloDiscovery[] Discovery);
