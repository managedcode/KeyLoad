using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Search;

internal static class TextRequestRefusalPublicAssertions
{
    internal static async Task RefusedAsync(RequestCqrsClusterCallers reader, SearchRequest request,
        string secret, CancellationToken token)
    {
        var sdk = await reader.Sdk.SearchAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Validation));
        var official = await reader.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.Validation, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(official, secret, TextClusterScenario.Secret);
    }

    internal static async Task HealthyAsync(RequestCqrsClusterCallers reader,
        TextClusterScenario scenario, CancellationToken token)
    {
        RankedDocument[] expected = [new(new(new(scenario.Partition, TextClusterScenario.Collection,
            TextRequestRefusalPublicTrial.Id), 1L, "{\"owner\":\"owner-a\"}", true,
            [TextClusterScenario.TextField, TextClusterScenario.VectorField, TextClusterScenario.SecretField]), 1d / 61d)];
        var request = scenario.Text(TextRequestRefusalPublicTrial.Term);
        await SqlClusterProtocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.SearchAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
            await reader.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers administrator,
        TextRequestRefusalPublicTrial.Original original, CancellationToken token)
    {
        await Assert.That(original.Receipt.CommandId).IsEqualTo(original.Command.CommandId);
        await Assert.That(original.Receipt.Token.AtomicPartitionId).IsEqualTo(original.Command.Partition.AtomicPartitionId);
        await Assert.That(original.Receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(original.Receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(original.Receipt.PersistedCopies).IsEqualTo(3);
        await SqlClusterProtocol.EqualAsync(new MutationReceipt[]
            { new("putDocument", TextClusterScenario.Collection, TextRequestRefusalPublicTrial.Id, 1L) },
            original.Receipt.Mutations.ToArray());
        await SqlClusterProtocol.EqualAsync(original.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(original.Command, token)));
        await SqlClusterProtocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value);
    }

    internal static async Task OutboxAsync(RequestCqrsClusterCallers administrator, TextClusterScenario scenario,
        OutboxStatus expected, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(scenario.Partition), token))).Value);
    }
}
