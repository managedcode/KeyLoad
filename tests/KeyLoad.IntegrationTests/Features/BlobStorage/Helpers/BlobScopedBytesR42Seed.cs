using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobScopedBytesR42Seed
{
    private const string FirstPartitionKey = "first";
    private const string ResourceA = "scoped-bytes-a-r42";
    private const string ResourceB = "scoped-bytes-b-r42";
    private const string ObjectA = "same-name";
    private const string ObjectB = "other-name";
    internal static async Task<BlobScopedBytesR42State[]> CreateAsync(RequestCqrsRf3Callers callers, CancellationToken token)
    {
        var first = new PartitionRef("scoped-bytes-r42-" + Guid.NewGuid().ToString("N"), "scoped-bytes-db", "scoped-bytes-domain", FirstPartitionKey);
        var second = first with { PartitionKey = "second" };
        var resource = new ResourceDefinition(ResourceA, ResourceKind.BlobStore, first.TransactionDomainId)
        { BlobPolicy = new() { MaxBlobBytes = 262212L, MaxReservedBytes = 262212L, MaxObjectKeys = 4, MaxVersions = 4, MaxUploads = 4 } };
        foreach (var item in new[] { resource, resource with { Name = ResourceB } })
        { await SqlRf3Protocol.EqualAsync(item, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(first.TenantId, first.DatabaseId, item), token))); }
        var upload = Guid.NewGuid();
        var states = new[]{new BlobScopedBytesR42State(0,new(first,ResourceA,ObjectA),upload),
   new BlobScopedBytesR42State(1,new(second,ResourceA,ObjectA),upload),
   new BlobScopedBytesR42State(2,new(first,ResourceB,ObjectA),upload),
   new BlobScopedBytesR42State(3,new(first,ResourceA,ObjectB),upload),
   new BlobScopedBytesR42State(4,new(second,ResourceB,"absent-name"),upload)};
        foreach (var state in states)
        {
            var request = new AtomicPartitionPlacementReadRequest(1, state.Blob.Partition);
            state.Placement = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
            await SqlRf3Protocol.EqualAsync(state.Placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(await callers.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
        }
        return states;
    }
}
