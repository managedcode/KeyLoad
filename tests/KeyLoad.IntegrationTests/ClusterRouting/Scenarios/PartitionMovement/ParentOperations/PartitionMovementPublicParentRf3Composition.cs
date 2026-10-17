using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementPublicParentRf3Composition
{
    internal const string CompositionQueue = "parent-composition-jobs";
    internal const string CompositionGraph = "parent-composition-links";
    internal const string CompositionMessage = "parent-original-link";
    internal const string EdgePrefix = "parent-edge-";
    internal const string ReversePrefix = "parent-derived-";

    internal static async Task RunAsync(PartitionMovementPublicParentRf3Seed seed,
        bool configureResources, CancellationToken token)
    {
        if (configureResources)
        {
            foreach (var (resource, kind) in new[] { (CompositionQueue, ResourceKind.WorkQueue),
                (CompositionGraph, ResourceKind.Graph) })
            {
                _ = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ConfigureResourceAsync(Guid.NewGuid(),
                    new(seed.Partition.TenantId, seed.Partition.DatabaseId,
                        new(resource, kind, seed.Partition.TransactionDomainId)), token));
            }
        }
        var link = new QueueGraphLink(seed.Models.First, seed.Models.Second, RelationalSqlRf3Tokens.EdgeLabel);
        var forward = seed.Models.Command(new EnqueueMessage(CompositionQueue, CompositionMessage,
            JsonSerializer.Serialize(link, JsonDefaults.Options)),
            new QueueToGraph(CompositionGraph, CompositionQueue, EdgePrefix));
        await seed.CommitAsync(forward, token);
        await seed.CommitAsync(seed.Models.Command(new GraphToQueueMutation(CompositionQueue, CompositionGraph,
            seed.Models.First, ReversePrefix)), token);
    }
}
