using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.IntegrationTests.StorageReplication;
using P = KeyLoad.IntegrationTests.ClientApi.McpAnalyticsDropDiscoveryProtocol;

namespace KeyLoad.IntegrationTests.ClientApi;

internal static class McpAnalyticsDropDiscoveryAssertions
{
    internal static async Task ReadsAsync(RequestCqrsClusterCallers caller,
        McpAnalyticsDropDiscoveryState state, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(P.Expected(state.Anchor), await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.ReadAnalyticsEventsAsync(P.Read(state.Partition), token)));
        await SqlClusterProtocol.EqualAsync(P.Expected(state.Anchor), (await McpCallerAssertions.SuccessAsync<AnalyticsEventsPage>(
            await caller.Mcp.CallAsync(AnalyticsStoreProtocol.ReadTool, P.Read(state.Partition), token))).Value);
        await SqlClusterProtocol.EqualAsync(P.Aggregate, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.AggregateAnalyticsAsync(P.Query(state.Partition), token)));
        await SqlClusterProtocol.EqualAsync(P.Aggregate, (await McpCallerAssertions.SuccessAsync<AnalyticsAggregateResult>(
            await caller.Mcp.CallAsync(AnalyticsStoreProtocol.AggregateTool, P.Query(state.Partition), token))).Value);
        await RowAsync(caller, state.Anchor, P.AnchorJson, token);
    }

    internal static async Task<EntityRef> CaptureAsync(RequestCqrsClusterCallers caller, EntityRef name,
        string expectedJson, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.GetAsync(name, token));
        await Assert.That(actual.Document).IsNotNull();
        var reference = actual.Document!.Reference;
        await Assert.That(reference.Partition).IsEqualTo(name.Partition);
        await Assert.That(reference.Collection).IsEqualTo(name.Collection);
        await Assert.That(reference.Id).IsEqualTo(name.Id);
        await Assert.That(reference.Lifetime).IsNotNull();
        await Assert.That(string.IsNullOrWhiteSpace(reference.Lifetime!.Incarnation)).IsFalse();
        await RowAsync(caller, reference, expectedJson, token);
        return reference;
    }

    internal static async Task RowAsync(RequestCqrsClusterCallers caller, EntityRef reference,
        string json, CancellationToken token)
    {
        var expected = new DocumentReadResult(new(reference, 1, json, false, []));
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.GetAsync(reference, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
            await caller.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
    }

    internal static async Task ReceiptAsync(CommandRequest command,
        CommitReceipt actual, MutationReceipt[] effects)
    {
        await SubmissionColdAssertions.AtomicPartitionAsync(command, actual);
        await Assert.That(actual.Token.Position).IsGreaterThan(0);
        await SubmissionColdAssertions.ReceiptAsync(actual,
            new(command.CommandId, actual.Token, [.. effects], DurabilityProfile.ProcessDurable, 3));
    }
}
