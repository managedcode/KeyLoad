using System.Security.Cryptography;
using System.Text;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class ProjectionPublicSetup
{
    internal static async Task<(PartitionRef Partition, McpPersistedIdentity Identity)> CreateAsync(
        RequestCqrsClusterCallers root, CancellationToken token)
    {
        var partition = new PartitionRef(ProjectionPublicProtocol.TenantPrefix + Guid.NewGuid().ToString(ProjectionPublicProtocol.GuidFormat),
            ProjectionPublicProtocol.Database, ProjectionPublicProtocol.Domain, Guid.NewGuid().ToString(ProjectionPublicProtocol.GuidFormat));
        foreach (var name in new[] { ProjectionPublicProtocol.Source, ProjectionPublicProtocol.Target })
        {
            var resource = new ResourceDefinition(name, ResourceKind.Collection, partition.TransactionDomainId);
            await ProjectionPublicAssertions.ExactAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, resource), token)));
        }
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
        return (partition, new(principal, credential, secret));
    }
}
