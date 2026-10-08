using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record NativeAnnMaintenanceRf3Scenario(PartitionRef Partition)
{
    internal const string Collection = "ann-admin-vectors";
    internal const string Field = "/embedding";
    internal const string Consumer = "ann-admin-consumer";
    internal const string Tool = "keyload_search_ann_maintain";
    internal const string Json = "{}";
    private const string Tenant = "ann-admin-tenant";
    private const string DatabasePrefix = "ann-admin-db-";
    private const string Domain = "ann-admin-domain";
    private const string Key = "ann-admin-partition";
    private const string GuidFormat = "N";
    private const int Dimension = 2;
    private const long Generation = 1;
    private const long Revision = 1;
    internal static VectorSpace Space { get; } = new("ann-admin-space", Dimension, DistanceMetric.DotProduct, "ann-admin-model", "v1");
    internal static readonly string[] Ids = ["ann-a", "ann-b", "ann-c"];

    internal NativeAnnMaintenanceRf3Scenario() : this(new PartitionRef(Tenant, DatabasePrefix + Guid.NewGuid().ToString(GuidFormat), Domain, Key)) { }

    internal async Task SeedAsync(KeyLoadClient sdk, CancellationToken token)
    {
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, Domain) { VectorProfiles = [new(Field, Space)] };
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(Partition.TenantId, Partition.DatabaseId, definition), token));
        var seed = new CommandRequest(Guid.NewGuid(), Partition,
            [new PutDocument(Collection, Ids[0], Json), new PutDocument(Collection, Ids[1], Json), new PutDocument(Collection, Ids[2], Json),
             new PutVector(Collection, Ids[0], Field, [1, 0], Space, Revision),
             new PutVector(Collection, Ids[1], Field, [0, 1], Space, Revision),
             new PutVector(Collection, Ids[2], Field, [-1, 0], Space, Revision)]);
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(seed, token));
    }
    internal async Task<AnnMaintenanceRequest> RequestAsync(KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken token)
    {
        var status = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        var official = (await McpCallerAssertions.SuccessAsync<NodeStatus>(await mcp.CallAsync(McpCallerTools.AdminStatus, (object?)null, token))).Value;
        await Assert.That(official.NodeId).IsEqualTo(status.NodeId);
        await Assert.That(official.Incarnation).IsEqualTo(status.Incarnation);
        if (!Guid.TryParse(status.NodeId, out var owner) || owner == Guid.Empty)
        { throw new InvalidOperationException("The actual administrative status does not expose a canonical physical owner."); }
        var placement = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAtomicPartitionPlacementAsync(new(1, Partition), token));
        return new(Guid.NewGuid(), new(Partition, Consumer), Collection, Field, Space, Generation, owner,
            new(placement.PhysicalShardId, placement.Incarnation, placement.VoterIds, placement.PlacementEpoch), AnnMaintenanceMode.Build);
    }
    internal SearchRequest Search() => new(Partition, Collection, VectorField: Field, Vector: [1, 0], Space: Space, Limit: Ids.Length);
}
