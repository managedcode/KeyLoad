using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record NativeTextMaintenanceRf3Scenario(PartitionRef Partition)
{
    internal const string Collection = "incremental-text";
    internal const string Field = "/text";
    internal const string Consumer = "incremental-text-consumer";
    internal const string Ukrainian = "ukrainian";
    internal const string English = "english";
    internal const string UkrainianJson = """{"text":"привіт світ"}""";
    internal const string EnglishJson = """{"text":"hello world"}""";
    internal const string ChangedJson = """{"text":"оновлено changed"}""";
    private const string Tenant = "incremental-text-tenant";
    private const string DatabasePrefix = "incremental-text-db-";
    private const string Domain = "incremental-text-domain";
    private const string Key = "incremental-text-partition";
    private const string GuidFormat = "N";
    private const long Generation = 1;
    private const int PlacementVersion = 1;
    private const string InvalidOwner = "The actual administrative status does not expose a canonical text-index owner.";

    internal static PartitionRef CreatePartition()
        => new(Tenant, DatabasePrefix + Guid.NewGuid().ToString(GuidFormat), Domain, Key);

    internal async Task SeedAsync(KeyLoadClient sdk, CancellationToken token)
    {
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(Partition.TenantId, Partition.DatabaseId,
                new(Collection, ResourceKind.Collection, Domain)), token));
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(new(Guid.NewGuid(), Partition,
            [new PutDocument(Collection, Ukrainian, UkrainianJson), new PutDocument(Collection, English, EnglishJson)]), token));
    }

    internal async Task<TextIndexMaintenanceRequest> RequestAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        var official = (await McpCallerAssertions.SuccessAsync<NodeStatus>(
            await mcp.CallWithoutBodyAsync(McpCallerTools.AdminStatus, token))).Value;
        await Assert.That(official.NodeId).IsEqualTo(actual.NodeId);
        await Assert.That(official.Incarnation).IsEqualTo(actual.Incarnation);
        if (!Guid.TryParse(actual.NodeId, out var node) || node == Guid.Empty)
        { throw new InvalidOperationException(InvalidOwner); }
        var placement = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAtomicPartitionPlacementAsync(
            new(PlacementVersion, Partition), token));
        return new(Guid.NewGuid(), new(Partition, Consumer), Collection, Field, Generation, node,
            new(placement.PhysicalShardId, placement.Incarnation, placement.VoterIds, placement.PlacementEpoch),
            TextIndexMaintenanceMode.Build);
    }
}
