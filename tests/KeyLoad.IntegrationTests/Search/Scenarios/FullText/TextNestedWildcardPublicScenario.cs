using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.IntegrationTests.ResourceExecution;

namespace KeyLoad.IntegrationTests.Search;

internal sealed record TextNestedWildcardPublicScenario(PartitionRef Partition,
    CommandRequest Original, CommitReceipt Receipt, McpPersistedIdentity Identity, EntityRefLifetime Lifetime)
{
    internal const string Collection = "nested-text";
    internal const string Field = "/profile/secret";
    internal const string ProfilePolicy = "/profile/*";
    internal const string ArrayPolicy = "/items/*/secret";
    internal const string Use = "nested.profile.use";
    internal const string ProfileRead = "nested.profile.read";
    internal const string ArrayRead = "nested.array.read";
    internal const string Full = "{\"profile\":{\"secret\":\"needle\",\"label\":\"private-label\"},\"items\":[{\"secret\":\"array-canary-a\",\"label\":\"one\"},{\"secret\":\"array-canary-b\",\"label\":\"two\"}],\"owner\":\"owner-a\"}";
    internal const string Hidden = "{\"profile\":{},\"items\":[{\"label\":\"one\"},{\"label\":\"two\"}],\"owner\":\"owner-a\"}";
    internal const string ProfileVisible = "{\"profile\":{\"secret\":\"needle\",\"label\":\"private-label\"},\"items\":[{\"label\":\"one\"},{\"label\":\"two\"}],\"owner\":\"owner-a\"}";

    internal SearchRequest Request() => new(Partition, Collection, Field, "needle", Limit: 1);

    internal EntityRef StoredReference(string id) => new(Partition, Collection, id) { Lifetime = Lifetime };

    internal static async Task<TextNestedWildcardPublicScenario> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var partition = new PartitionRef("nested-text-" + Guid.NewGuid().ToString("N"),
            "nested-db", "nested-domain", Guid.NewGuid().ToString("N"));
        var resource = new ResourceDefinition(Collection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            FieldPolicies = [new(ProfilePolicy, "nested", RawReadGrant: ProfileRead, RawUseGrant: Use),
                new(ArrayPolicy, "array", RawReadGrant: ArrayRead, RawUseGrant: "nested.array.use")]
        };
        var creationId = Guid.NewGuid();
        var creation = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigureResourceAsync(creationId, creation, token));
        var original = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(Collection, "a", Full, Access: new("owner-a"))]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(original, token));
        var principal = new PrincipalRecord("nested-reader-" + Guid.NewGuid().ToString("N"), partition.TenantId,
            [new(partition.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead)], [Use])
        {
            RestrictRows = true,
            OwnerId = "owner-a"
        };
        var identity = await TextClusterScenario.ConfigureIdentityAsync(fixture, principal, token);
        await Assert.That(identity.Principal.PolicyEpoch).IsEqualTo(1L);
        await SqlClusterProtocol.EqualAsync(new[] { new MutationReceipt("putDocument", Collection, "a", 1L) },
            receipt.Mutations.ToArray());
        await Assert.That(receipt.CommandId).IsEqualTo(original.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        return new(partition, original, receipt, identity, ResourceCreationLifetime.Create(creationId, creation));
    }
}
