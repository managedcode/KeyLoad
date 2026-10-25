using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.RelationalStorage;

internal static class LeftFilteredJoinRefusals
{
    private const string MinimumFieldKey = "minimum";
    internal static async Task DeniedAsync(RequestCqrsClusterCallers callers, LeftFilteredJoinState state,
        CancellationToken token)
    {
        var failure = await callers.Sdk.QueryAsync(LeftFilteredJoinProtocol.Query(state.Partition), token);
        await Assert.That(failure.IsSuccess).IsFalse();
        await Assert.That(failure.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        _ = (await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName,
            LeftFilteredJoinProtocol.Request(state.Partition), token), ErrorCode.PermissionDenied, dispatched: true)).Value;
    }

    internal static async Task InvalidAsync(RequestCqrsClusterCallers callers, LeftFilteredJoinState state,
        CancellationToken token)
    {
        var request = LeftFilteredJoinProtocol.Request(state.Partition);
        // The unified SQL tool compiles the text before dispatch, so the unsupported predicate is refused there without a
        // request ID; the argument refusals need the stored model and are dispatched.
        foreach (var (input, code, dispatched) in new[]
        {
            (request with { Parameters = null }, ErrorCode.Validation, true),
            (request with { Sql = LeftFilteredJoinProtocol.Prefix + "WHERE r.name = @minimum "
                + LeftFilteredJoinProtocol.Order }, ErrorCode.UnsupportedCapability, false),
            (request with { Parameters = new() { [MinimumFieldKey] = System.Text.Json.JsonSerializer.SerializeToElement("wrong") } },
                ErrorCode.Validation, true)
        })
        {
            var failure = await callers.Sdk.ExecuteSqlAsync(input, token);
            await Assert.That(failure.IsSuccess).IsFalse();
            await Assert.That(failure.Problem?.ErrorCode).IsEqualTo(code.ToString());
            _ = (await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName,
                input, token), code, dispatched)).Value;
        }
    }

    internal static async Task SourcesAsync(KeyLoadClient sdk, LeftFilteredJoinState state, CancellationToken token)
    {
        foreach (var (collection, id, json) in new[]
        {
            (LeftFilteredJoinProtocol.Left, "a", LeftFilteredJoinProtocol.LeftA),
            (LeftFilteredJoinProtocol.Left, "b", LeftFilteredJoinProtocol.LeftB),
            (LeftFilteredJoinProtocol.Right, "c1", LeftFilteredJoinProtocol.RightA),
            (LeftFilteredJoinProtocol.Right, "c2", LeftFilteredJoinProtocol.RightB)
        })
        {
            var actual = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(new(state.Partition, collection, id), token))
                ?? throw new InvalidOperationException("The original typed source row is missing.");
            await SqlClusterProtocol.EqualAsync(new DocumentResult(state.Stored(collection, id), 1, json, false, []), actual);
        }
        await SqlClusterProtocol.EqualAsync(state.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(state.Seed, token)));
    }
}
