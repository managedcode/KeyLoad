using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Search;

internal static class TextRowOwnershipPublicAssertions
{
    internal static async Task PagesAsync(RequestCqrsClusterCallers reader, TextClusterScenario scenario,
        long revision, CancellationToken token)
    {
        RankedDocument[] expected = [new(new(new(scenario.Partition, TextClusterScenario.Collection,
            TextClusterScenario.FirstId), revision, "{\"owner\":\"owner-a\"}", true,
            [TextClusterScenario.TextField, TextClusterScenario.VectorField,
                TextClusterScenario.SecretField]), 1d / 61d)];
        foreach (var term in new[] { "needle", "Київ" })
        {
            await PageAsync(reader, scenario.Text(term), expected, token);
        }
    }

    internal static async Task EmptyAsync(RequestCqrsClusterCallers reader,
        TextClusterScenario scenario, CancellationToken token)
    {
        foreach (var term in new[] { "needle", "Київ" })
        {
            await PageAsync(reader, scenario.Text(term), [], token);
        }
    }

    private static async Task PageAsync(RequestCqrsClusterCallers reader, SearchRequest request,
        RankedDocument[] expected, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.SearchAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
            await reader.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value);
    }

    internal static async Task ReceiptAsync(TextRowOwnershipPublicTrial.Original original, long revision)
    {
        await Assert.That(original.Receipt.CommandId).IsEqualTo(original.Command.CommandId);
        await Assert.That(original.Receipt.Token.AtomicPartitionId).IsEqualTo(original.Command.Partition.AtomicPartitionId);
        await Assert.That(original.Receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(original.Receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(original.Receipt.PersistedCopies).IsEqualTo(3);
        await SqlClusterProtocol.EqualAsync(new MutationReceipt[]
            { new("putDocument", TextClusterScenario.Collection, TextClusterScenario.FirstId, revision) },
            original.Receipt.Mutations.ToArray());
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers administrator,
        TextRowOwnershipPublicTrial.Original original, long revision, CancellationToken token)
    {
        await ReceiptAsync(original, revision);
        await SqlClusterProtocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.CommitAsync(original.Command, token)));
        await SqlClusterProtocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value);
    }
}
