using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextRowOwnershipPublicAssertions
{
    internal static async Task PagesAsync(RequestCqrsRf3Callers reader, NativeTextRf3Scenario scenario,
        long revision, CancellationToken token)
    {
        RankedDocument[] expected = [new(new(new(scenario.Partition, NativeTextRf3Scenario.Collection,
            NativeTextRf3Scenario.FirstId), revision, "{\"owner\":\"owner-a\"}", true,
            [NativeTextRf3Scenario.TextField, NativeTextRf3Scenario.VectorField,
                NativeTextRf3Scenario.SecretField]), 1d / 61d)];
        foreach (var term in new[] { "needle", "Київ" })
        {
            await PageAsync(reader, scenario.Text(term), expected, token);
        }
    }

    internal static async Task EmptyAsync(RequestCqrsRf3Callers reader,
        NativeTextRf3Scenario scenario, CancellationToken token)
    {
        foreach (var term in new[] { "needle", "Київ" })
        {
            await PageAsync(reader, scenario.Text(term), [], token);
        }
    }

    private static async Task PageAsync(RequestCqrsRf3Callers reader, SearchRequest request,
        RankedDocument[] expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.SearchAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
            await reader.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value);
    }

    internal static async Task ReceiptAsync(NativeTextRowOwnershipPublicTrial.Original original, long revision)
    {
        await Assert.That(original.Receipt.CommandId).IsEqualTo(original.Command.CommandId);
        await Assert.That(original.Receipt.Token.AtomicPartitionId).IsEqualTo(original.Command.Partition.AtomicPartitionId);
        await Assert.That(original.Receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(original.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(new MutationReceipt[]
            { new("putDocument", NativeTextRf3Scenario.Collection, NativeTextRf3Scenario.FirstId, revision) },
            original.Receipt.Mutations.ToArray());
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers administrator,
        NativeTextRowOwnershipPublicTrial.Original original, long revision, CancellationToken token)
    {
        await ReceiptAsync(original, revision);
        await SqlRf3Protocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.CommitAsync(original.Command, token)));
        await SqlRf3Protocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value);
    }
}
