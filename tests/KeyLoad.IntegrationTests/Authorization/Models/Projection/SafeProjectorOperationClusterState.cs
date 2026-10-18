using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record SafeProjectorOperationClusterState(PartitionRef Partition, string Collection, string Reader,
    string Key, ResourceDefinition Resource, Guid ResourceId, Guid PrincipalId, PrincipalRecord InitialPrincipal,
    Guid CredentialId, ApiKeyRecord Credential, CommandRequest Seed, CommitReceipt SeedReceipt)
{
    internal EntityRef First => new(Partition, Collection, SafeProjectorOperationClusterProtocol.First);
    internal EntityRef Second => new(Partition, Collection, SafeProjectorOperationClusterProtocol.Second);

    internal PrincipalRecord Principal(long epoch, ImmutableArray<string> grants)
        => new(Reader, Partition.TenantId,
            [new(Partition.DatabaseId, Collection,
                Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query | Capability.ChangesRead)], grants)
        {
            PolicyEpoch = epoch
        };

    public override string ToString() => nameof(SafeProjectorOperationClusterState);
}
