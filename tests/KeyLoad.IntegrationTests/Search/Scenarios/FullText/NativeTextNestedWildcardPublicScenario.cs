using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record NativeTextNestedWildcardPublicScenario(PartitionRef Partition,
    CommandRequest Original, CommitReceipt Receipt, McpPersistedIdentity Identity)
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

    internal static async Task<NativeTextNestedWildcardPublicScenario> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var partition = new PartitionRef("nested-text-" + Guid.NewGuid().ToString("N"),
            "nested-db", "nested-domain", Guid.NewGuid().ToString("N"));
        var resource = new ResourceDefinition(Collection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            FieldPolicies = [new(ProfilePolicy, "nested", RawReadGrant: ProfileRead, RawUseGrant: Use),
                new(ArrayPolicy, "array", RawReadGrant: ArrayRead, RawUseGrant: "nested.array.use")]
        };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource), token));
        var original = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(Collection, "a", Full, Access: new("owner-a"))]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(original, token));
        var principal = new PrincipalRecord("nested-reader-" + Guid.NewGuid().ToString("N"), partition.TenantId,
            [new(partition.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead)], [Use])
        {
            RestrictRows = true,
            OwnerId = "owner-a"
        };
        var identity = await NativeTextRf3Scenario.ConfigureIdentityAsync(fixture, principal, token);
        await Assert.That(identity.Principal.PolicyEpoch).IsEqualTo(1L);
        await SqlRf3Protocol.EqualAsync(new[] { new MutationReceipt("putDocument", Collection, "a", 1L) },
            receipt.Mutations.ToArray());
        await Assert.That(receipt.CommandId).IsEqualTo(original.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        return new(partition, original, receipt, identity);
    }
}
