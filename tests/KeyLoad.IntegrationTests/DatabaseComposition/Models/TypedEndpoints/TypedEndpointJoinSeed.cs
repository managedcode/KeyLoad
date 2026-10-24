using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal sealed record TypedEndpointJoinSeed(PartitionRef Partition, ResourceDefinition ToDefinition,
    McpPersistedIdentity Identity, PrincipalRecord Principal, TypedEndpointJoinModels Models,
    EntityRefLifetime FromLifetime, EntityRefLifetime ToLifetime)
{
    internal EntityRef StoredFrom(string id) => new(Partition, TypedEndpointJoinProtocol.FromCollection, id) { Lifetime = FromLifetime };
    internal EntityRef StoredTo(string id) => new(Partition, TypedEndpointJoinProtocol.ToCollection, id) { Lifetime = ToLifetime };
}
