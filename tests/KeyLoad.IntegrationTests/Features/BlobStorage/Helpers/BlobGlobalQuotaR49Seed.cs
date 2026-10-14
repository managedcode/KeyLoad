using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobGlobalQuotaR49Seed
{
    private const string TenantPrefix = "blob-global-";
    private const string Database = "global-quota";
    private const string Domain = "global-domain";
    internal static async Task<BlobGlobalQuotaR49State> RunAsync(RequestCqrsRf3Callers callers, CancellationToken token)
    {
        var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString("N"), Database, Domain, Guid.NewGuid().ToString("N"));
        var request = new AtomicPartitionPlacementReadRequest(1, partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
        await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(await callers.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
        await Assert.That(placement.Revision).IsEqualTo(0L);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.OutboxStatusAsync(partition, token));
        await SqlRf3Protocol.EqualAsync(outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await callers.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(partition), token))).Value);
        var state = new BlobGlobalQuotaR49State(partition, placement, outbox);
        string[] names = [BlobGlobalQuotaR49Corpus.FirstResource, BlobGlobalQuotaR49Corpus.SecondResource, BlobGlobalQuotaR49Corpus.ControlResource];
        long[] limits = [BlobGlobalQuotaR49Corpus.FirstReservation, BlobGlobalQuotaR49Corpus.SecondReservation, BlobGlobalQuotaR49Corpus.SmallLength];
        for (var index = 0; index < names.Length; index++)
        {
            var definition = new ResourceDefinition(names[index], ResourceKind.BlobStore, partition.TransactionDomainId)
            { BlobPolicy = new() { MaxBlobBytes = limits[index], MaxReservedBytes = limits[index] } };
            await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, definition), token)));
            state.ConfiguredResources.Add(definition.Name);
            await BlobGlobalQuotaR49Current.RunAsync(callers, state, token);
        }
        await BlobGlobalQuotaR49Operations.PublishAsync(callers, state, state.Control, token);
        await BlobGlobalQuotaR49Current.WindowsAsync(callers, state, token);
        return state;
    }
}
