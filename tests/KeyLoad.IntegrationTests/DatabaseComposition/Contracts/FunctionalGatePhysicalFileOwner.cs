using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGatePhysicalFileOwner(string Resource, string Container, string Directory,
    Guid NodeId, Guid Incarnation, ContainerRuntimeInspection Runtime);
