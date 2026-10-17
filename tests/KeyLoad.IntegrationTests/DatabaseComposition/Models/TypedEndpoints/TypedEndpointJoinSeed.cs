using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record TypedEndpointJoinSeed(PartitionRef Partition, ResourceDefinition ToDefinition,
    McpPersistedIdentity Identity, PrincipalRecord Principal, TypedEndpointJoinModels Models);
