using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.ClientApi.McpAnalyticsDropDiscoveryProtocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class McpAnalyticsDropDiscoveryAssertions
{
    internal static async Task ReadsAsync(RequestCqrsRf3Callers caller,
        McpAnalyticsDropDiscoveryState state, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(P.Expected(state.Anchor), await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.ReadAnalyticsEventsAsync(P.Read(state.Partition), token)));
        await SqlRf3Protocol.EqualAsync(P.Expected(state.Anchor), (await McpCallerAssertions.SuccessAsync<AnalyticsEventsPage>(
            await caller.Mcp.CallAsync(AnalyticsStoreProtocol.ReadTool, P.Read(state.Partition), token))).Value);
        await SqlRf3Protocol.EqualAsync(P.Aggregate, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.AggregateAnalyticsAsync(P.Query(state.Partition), token)));
        await SqlRf3Protocol.EqualAsync(P.Aggregate, (await McpCallerAssertions.SuccessAsync<AnalyticsAggregateResult>(
            await caller.Mcp.CallAsync(AnalyticsStoreProtocol.AggregateTool, P.Query(state.Partition), token))).Value);
        await RowAsync(caller, state.Anchor, P.AnchorJson, token);
    }

    internal static async Task<EntityRef> CaptureAsync(RequestCqrsRf3Callers caller, EntityRef name,
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

    internal static async Task RowAsync(RequestCqrsRf3Callers caller, EntityRef reference,
        string json, CancellationToken token)
    {
        var expected = new DocumentReadResult(new(reference, 1, json, false, []));
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.GetAsync(reference, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
            await caller.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
    }

    internal static async Task ReceiptAsync(RequestCqrsRf3Callers caller, CommandRequest command,
        CommitReceipt actual, MutationReceipt[] effects, CancellationToken token)
    {
        await NativeSubmissionColdAssertions.PlacementAsync(caller, command, actual, token);
        await Assert.That(actual.Token.Position).IsGreaterThan(0);
        await NativeSubmissionColdAssertions.ReceiptAsync(actual,
            new(command.CommandId, actual.Token, [.. effects], DurabilityProfile.QuorumProcessDurable));
    }
}
