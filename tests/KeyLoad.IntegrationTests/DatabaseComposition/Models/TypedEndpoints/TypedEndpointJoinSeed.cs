using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal sealed record TypedEndpointJoinSeed(PartitionRef Partition, ResourceDefinition ToDefinition,
    McpPersistedIdentity Identity, PrincipalRecord Principal, TypedEndpointJoinModels Models);
