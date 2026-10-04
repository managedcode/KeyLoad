using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record DueFaultRf3RestartState(
    NodeEpochRf3NodeObservation[] Status,
    ReplicaSiloDiscovery[] Discovery);
