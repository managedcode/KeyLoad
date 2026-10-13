using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextRequestRefusalPublicAssertions
{
    internal static async Task RefusedAsync(RequestCqrsRf3Callers reader, SearchRequest request,
        string secret, CancellationToken token)
    {
        var sdk = await reader.Sdk.SearchAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Validation));
        var official = await reader.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.Validation, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(official, secret, NativeTextRf3Scenario.Secret);
    }

    internal static async Task HealthyAsync(RequestCqrsRf3Callers reader,
        NativeTextRf3Scenario scenario, CancellationToken token)
    {
        RankedDocument[] expected = [new(new(new(scenario.Partition, NativeTextRf3Scenario.Collection,
            NativeTextRequestRefusalPublicTrial.Id), 1L, "{\"owner\":\"owner-a\"}", true,
            [NativeTextRf3Scenario.TextField, NativeTextRf3Scenario.VectorField, NativeTextRf3Scenario.SecretField]), 1d / 61d)];
        var request = scenario.Text(NativeTextRequestRefusalPublicTrial.Term);
        await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.SearchAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
            await reader.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers administrator,
        NativeTextRequestRefusalPublicTrial.Original original, CancellationToken token)
    {
        await Assert.That(original.Receipt.CommandId).IsEqualTo(original.Command.CommandId);
        await Assert.That(original.Receipt.Token.AtomicPartitionId).IsEqualTo(original.Command.Partition.AtomicPartitionId);
        await Assert.That(original.Receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(original.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(new MutationReceipt[]
            { new("putDocument", NativeTextRf3Scenario.Collection, NativeTextRequestRefusalPublicTrial.Id, 1L) },
            original.Receipt.Mutations.ToArray());
        await SqlRf3Protocol.EqualAsync(original.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(original.Command, token)));
        await SqlRf3Protocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value);
    }

    internal static async Task OutboxAsync(RequestCqrsRf3Callers administrator, NativeTextRf3Scenario scenario,
        OutboxStatus expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(scenario.Partition), token))).Value);
    }
}
