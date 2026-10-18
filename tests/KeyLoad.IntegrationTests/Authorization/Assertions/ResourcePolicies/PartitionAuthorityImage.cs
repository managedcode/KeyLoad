using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PartitionAuthorityImage
{
    internal static async Task<JsonElement> CaptureAsync(KeyLoadClient root, PartitionAuthorityState state,
        CancellationToken token)
    {
        var images = new List<JsonElement>();
        foreach (var row in new[] {
            (PartitionAuthorityProtocol.FirstLeaf, PartitionAuthorityProtocol.FirstId, PartitionAuthorityProtocol.FirstJson),
            (PartitionAuthorityProtocol.SecondLeaf, PartitionAuthorityProtocol.SecondId, PartitionAuthorityProtocol.SecondJson) })
        {
            var partition = state.Scenario.Partitions[row.Item1];
            var reference = new EntityRef(partition, PartitionQueryClusterProtocol.Collection, row.Item2);
            var document = await PublicReadAssertions.DocumentAsync(await root.GetAsync(reference, token));
            await Assert.That(document).IsNotNull();
            await SqlClusterProtocol.EqualAsync(new DocumentResult(reference, PartitionQueryClusterProtocol.RowRevision,
                row.Item3, false, []), document);
            var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(partition, token));
            images.Add(JsonSerializer.SerializeToElement(new
            {
                Document = document,
                Outbox = outbox
            }, JsonDefaults.Options));
        }
        return JsonSerializer.SerializeToElement(images, JsonDefaults.Options);
    }
}
