using System.Security.Cryptography;
using System.Text;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.ResourceExecution;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal static class ProjectionPublicSetup
{
    internal static async Task<(PartitionRef Partition, McpPersistedIdentity Identity, EntityRefLifetime SourceLifetime,
        EntityRefLifetime TargetLifetime)> CreateAsync(RequestCqrsClusterCallers root, CancellationToken token)
    {
        var partition = new PartitionRef(ProjectionPublicProtocol.TenantPrefix + Guid.NewGuid().ToString(ProjectionPublicProtocol.GuidFormat),
            ProjectionPublicProtocol.Database, ProjectionPublicProtocol.Domain, Guid.NewGuid().ToString(ProjectionPublicProtocol.GuidFormat));
        var sourceLifetime = await ConfigureAsync(root, partition, ProjectionPublicProtocol.Source, token);
        var targetLifetime = await ConfigureAsync(root, partition, ProjectionPublicProtocol.Target, token);
        var principal = new PrincipalRecord(ProjectionPublicProtocol.IdentityPrefix + Guid.NewGuid().ToString(ProjectionPublicProtocol.GuidFormat),
            partition.TenantId, [new(partition.DatabaseId, ProjectionPublicProtocol.Source, Capability.All),
                new(partition.DatabaseId, ProjectionPublicProtocol.Target, Capability.All)], [])
        {
            ClusterAdministrator = true
        };
        await ProjectionPublicAssertions.ExactAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
        var key = ProjectionPublicProtocol.KeyPrefix + Guid.NewGuid().ToString(ProjectionPublicProtocol.GuidFormat);
        var secret = key + ProjectionPublicProtocol.Separator + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(ProjectionPublicProtocol.SecretBytes));
        var credential = new ApiKeyRecord(key, principal.Id, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(), credential, token));
        return (partition, new(principal, credential, secret), sourceLifetime, targetLifetime);
    }

    private static async Task<EntityRefLifetime> ConfigureAsync(RequestCqrsClusterCallers root, PartitionRef partition,
        string name, CancellationToken token)
    {
        var resource = new ResourceDefinition(name, ResourceKind.Collection, partition.TransactionDomainId);
        var creationId = Guid.NewGuid();
        var creation = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource);
        await ProjectionPublicAssertions.ExactAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigureResourceAsync(creationId, creation, token)));
        return ResourceCreationLifetime.Create(creationId, creation);
    }
}
