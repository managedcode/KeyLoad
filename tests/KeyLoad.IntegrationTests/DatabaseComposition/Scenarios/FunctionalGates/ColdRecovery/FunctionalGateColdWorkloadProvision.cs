using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateColdWorkloadProvision
{
    internal const int AdditionalIdentities = 5;
    internal const int TotalWorkloadIdentities = 8;

    internal static async Task<McpPersistedIdentity[]> CreateAsync(RequestCqrsClusterCallers administrator,
        PartitionRef partition, Capability read, CancellationToken token)
    {
        var identities = new List<McpPersistedIdentity>();
        for (var index = 0; index < AdditionalIdentities; index++)
        {
            var resource = Resource(index);
            var definition = new ResourceDefinition(resource, ResourceKind.Collection, partition.TransactionDomainId);
            await SqlClusterProtocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                    new(partition.TenantId, partition.DatabaseId, definition), token)));
            var identity = await FunctionalGateColdProvision.IdentityAsync(administrator, partition, read, false, token);
            var expected = identity.Principal with
            {
                PolicyEpoch = checked(identity.Principal.PolicyEpoch + 1),
                Grants = [.. identity.Principal.Grants,
                    new(partition.DatabaseId, resource, Capability.DocumentsRead | Capability.DocumentsWrite)]
            };
            var observed = await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
            await SqlClusterProtocol.EqualAsync(expected, observed);
            identities.Add(identity with
            {
                Principal = observed
            });
        }
        return [.. identities];
    }

    internal static string Resource(int index) => FunctionalGateColdProtocol.Resource("workload", index);

    internal static async Task InventoryAsync(FunctionalGateColdState state)
    {
        var identities = new[] { state.Writer, state.Reader, state.QueueOperator }.Concat(state.AdditionalIdentities).ToArray();
        await Assert.That(state.AdditionalIdentities.Length).IsEqualTo(AdditionalIdentities);
        await Assert.That(identities.Length).IsEqualTo(TotalWorkloadIdentities);
        await Assert.That(identities.Select(item => item.Principal.Id).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(TotalWorkloadIdentities);
        await Assert.That(identities.Select(item => item.Credential.Id).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(TotalWorkloadIdentities);
        await Assert.That(identities.Select(item => item.Secret).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(TotalWorkloadIdentities);
        foreach (var identity in state.AdditionalIdentities)
        {
            await Assert.That(identity.Principal.ClusterAdministrator).IsFalse();
            await Assert.That(identity.Credential.PrincipalId).IsEqualTo(identity.Principal.Id);
            await Assert.That(identity.Principal.TenantId).IsEqualTo(state.Partition.TenantId);
        }
    }
}
