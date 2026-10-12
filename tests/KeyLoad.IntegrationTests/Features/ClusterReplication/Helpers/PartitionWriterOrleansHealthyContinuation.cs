using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>A fresh authorized global write and its exact public replay survive the subsequent original cold restart.</summary>
internal sealed class PartitionWriterOrleansHealthyContinuation(PartitionMovementPublicParentRf3Seed seed)
{
    private const string ResourceName = "native-writer-after-retirement";
    private readonly Guid identity = Guid.NewGuid();
    private readonly ConfigureResourceRequest request = new(seed.Partition.TenantId, seed.Partition.DatabaseId,
        new(ResourceName, ResourceKind.Collection, seed.Partition.TransactionDomainId));
    private ResourceDefinition? original;

    internal async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ConfigureResourceAsync(identity,
            request, cancellationToken).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(request.Definition, actual);
        if (original is { } retained)
        { await SqlRf3Protocol.EqualAsync(retained, actual); }
        else
        { original = actual; }
        var official = (await McpCallerAssertions.SuccessAsync<ResourceDefinition>(await seed.Official.CallAsync(
            McpCallerTools.ResourcesConfigure, new { commandId = identity, request }, cancellationToken)
            .ConfigureAwait(false))).Value;
        await SqlRf3Protocol.EqualAsync(actual, official);
    }
}
