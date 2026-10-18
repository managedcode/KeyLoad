using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextRowOwnershipPublicAssertions
{
    internal static async Task PagesAsync(RequestCqrsClusterCallers reader, NativeTextClusterScenario scenario,
        long revision, CancellationToken token)
    {
        RankedDocument[] expected = [new(new(new(scenario.Partition, NativeTextClusterScenario.Collection,
            NativeTextClusterScenario.FirstId), revision, "{\"owner\":\"owner-a\"}", true,
            [NativeTextClusterScenario.TextField, NativeTextClusterScenario.VectorField,
                NativeTextClusterScenario.SecretField]), 1d / 61d)];
        foreach (var term in new[] { "needle", "Київ" })
        {
            await PageAsync(reader, scenario.Text(term), expected, token);
        }
    }

    internal static async Task EmptyAsync(RequestCqrsClusterCallers reader,
        NativeTextClusterScenario scenario, CancellationToken token)
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

    internal static async Task ReceiptAsync(NativeTextRowOwnershipPublicTrial.Original original, long revision)
    {
        await Assert.That(original.Receipt.CommandId).IsEqualTo(original.Command.CommandId);
        await Assert.That(original.Receipt.Token.AtomicPartitionId).IsEqualTo(original.Command.Partition.AtomicPartitionId);
        await Assert.That(original.Receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(original.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlClusterProtocol.EqualAsync(new MutationReceipt[]
            { new("putDocument", NativeTextClusterScenario.Collection, NativeTextClusterScenario.FirstId, revision) },
            original.Receipt.Mutations.ToArray());
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers administrator,
        NativeTextRowOwnershipPublicTrial.Original original, long revision, CancellationToken token)
    {
        await ReceiptAsync(original, revision);
        await SqlClusterProtocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.CommitAsync(original.Command, token)));
        await SqlClusterProtocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value);
    }
}
