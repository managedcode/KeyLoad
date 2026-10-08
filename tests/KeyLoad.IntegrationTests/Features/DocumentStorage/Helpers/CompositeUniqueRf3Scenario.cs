using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal sealed record CompositeUniqueRf3Scenario(PartitionRef Partition, McpPersistedIdentity Identity)
{
    internal const string Collection = "composite-unique";
    internal const string Index = "by-label-rank";
    internal const string First = "first";
    internal const string Second = "second";
    internal const string Third = "third";
    internal const string FirstPartition = "first-atomic-partition";
    internal const string OtherPartition = "other-atomic-partition";
    internal const string Alpha = "{\"label\":\"alpha\",\"rank\":1}";
    internal const string AlphaOther = "{\"label\":\"alpha\",\"rank\":2}";
    internal const string Delta = "{\"label\":\"delta\",\"rank\":4}";
    internal const string Epsilon = "{\"label\":\"epsilon\",\"rank\":5}";
    internal const string ConflictJson = "{\"label\":\"private-rollback-canary\",\"rank\":9}";
    internal const Capability Grants = Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query;
    internal PartitionRef Other => Partition with { PartitionKey = OtherPartition };
    internal static CommandRequest Command(PartitionRef partition, params Mutation[] mutations)
        => new(Guid.NewGuid(), partition, [.. mutations]);

    internal static async Task<CompositeUniqueRf3Scenario> CreateAsync(ClusterFixture fixture,
        KeyLoadClient admin, CancellationToken token)
    {
        var partition = new PartitionRef("kl011-" + Guid.NewGuid().ToString("N"), "database", "documents", FirstPartition);
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, partition.TransactionDomainId)
        { Indexes = [new(Index, ["/label", "/rank"], Unique: true)] };
        var configured = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, definition), token));
        await Assert.That(JsonDefaults.Serialize(configured.Indexes).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(definition.Indexes))).IsTrue();
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, Collection, Grants, token);
        return new(partition, identity);
    }

    internal async Task SetGrantsAsync(KeyLoadClient admin, Capability capabilities, long epoch, CancellationToken token)
    {
        var principal = Identity.Principal with
        { Grants = [new(Partition.DatabaseId, Collection, capabilities)], PolicyEpoch = epoch };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await Assert.That(persisted.PolicyEpoch).IsEqualTo(epoch);
        await Assert.That(persisted.Grants.Single().Capabilities).IsEqualTo(capabilities);
    }
}
