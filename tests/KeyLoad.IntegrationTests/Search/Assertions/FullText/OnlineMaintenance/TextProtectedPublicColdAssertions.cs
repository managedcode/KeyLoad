using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class TextProtectedPublicColdAssertions
{
    internal static async Task HealthyAsync(RequestCqrsClusterCallers reader,
        TextProtectedPublicColdState state, long epoch, CancellationToken token)
    {
        var wait = new WaitForIndexRequest(state.Scenario.Partition, TextClusterScenario.Collection,
            TextClusterScenario.TextField, state.Deleted.Token);
        foreach (var result in new[]
        {
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.WaitForIndexAsync(wait, token)),
            (await McpCallerAssertions.SuccessAsync<WaitForIndexResult>(
                await reader.Mcp.CallAsync(WaitForIndexProtocol.Tool, wait, token))).Value
        })
        {
            await Assert.That(result.PolicyEpoch).IsEqualTo(epoch);
            await Assert.That(result.SchemaVersion).IsEqualTo(state.Scenario.Resource.SchemaVersion);
            await Assert.That(result.AppliedToken.Incarnation).IsEqualTo(state.Deleted.Token.Incarnation);
            await Assert.That(result.AppliedToken.AtomicPartitionId).IsEqualTo(state.Deleted.Token.AtomicPartitionId);
            await Assert.That(result.AppliedToken.Position).IsGreaterThanOrEqualTo(state.Deleted.Token.Position);
        }
        var reference = new EntityRef(state.Scenario.Partition, TextClusterScenario.Collection,
            TextClusterScenario.FirstId);
        RankedDocument[] expected = [new(new(reference, 2, "{\"owner\":\"owner-a\"}", true,
            [TextClusterScenario.TextField, TextClusterScenario.VectorField, TextClusterScenario.SecretField]), 1d / 61d)];
        await PageAsync(reader, state.Scenario.Text("fresh"), expected, token);
        await PageAsync(reader, state.Scenario.Text("Львів"), expected, token);
        await PageAsync(reader, state.Scenario.Text("needle"), [], token);
        await PageAsync(reader, state.Scenario.Text("Київ"), [], token);
    }

    private static async Task PageAsync(RequestCqrsClusterCallers reader, SearchRequest request,
        RankedDocument[] expected, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.SearchAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
            await reader.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token))).Value);
    }

    internal static async Task DeniedAsync(RequestCqrsClusterCallers reader, RequestCqrsClusterCallers administrator,
        TextProtectedPublicColdState state, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
        var request = state.Scenario.Text("fresh");
        var denied = await reader.Sdk.SearchAsync(request, token);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token),
            ErrorCode.PermissionDenied, dispatched: true);
        await SqlClusterProtocol.EqualAsync(before, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        await SqlClusterProtocol.EqualAsync(before, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }
}
