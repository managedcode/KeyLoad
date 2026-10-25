using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class EventReferenceSelectFlow
{
    internal static Task ReaderAsync(ClusterFixture fixture, EventReferenceSelectState state,
        bool denied, bool masked, CancellationToken token)
        => WithReaderAsync(fixture, state, async callers =>
        {
            if (denied)
            {
                await FailureAsync(callers, EventReferenceSelectProtocol.Operation(state.Partition), ErrorCode.PermissionDenied, token);
                await FailureAsync(callers, EventReferenceSelectProtocol.Operation(state.Partition, EventReferenceSelectProtocol.EmptyStreamSet), ErrorCode.PermissionDenied, token);
            }
            else
            {
                await EventReferenceSelectAssertions.PageAsync(callers, state, masked, token);
                var empty = await SqlClusterProtocol.SdkAsync<QueryPage>(callers.Sdk,
                    EventReferenceSelectProtocol.Operation(state.Partition, EventReferenceSelectProtocol.EmptyStreamSet), token);
                await Assert.That(empty.Rows).IsEmpty();
                await Assert.That(empty.Cursor).IsNull();
                await EventReferenceSelectAssertions.SamePageAsync(empty,
                    await SqlClusterProtocol.McpAsync<QueryPage>(callers.Mcp,
                        EventReferenceSelectProtocol.Operation(state.Partition, EventReferenceSelectProtocol.EmptyStreamSet), token));
                await FailureAsync(callers, EventReferenceSelectProtocol.Operation(state.Partition) with
                {
                    Parameters = null
                }, ErrorCode.Validation, token);
                await EventReferenceSelectAssertions.PageAsync(callers, state, masked, token);
            }
        }, token);

    internal static Task AdminAsync(ClusterFixture fixture, EventReferenceSelectState state, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            await FailureAsync(admin, EventReferenceSelectProtocol.Operation(state.Partition, EventReferenceSelectProtocol.InvalidStreamSet),
                ErrorCode.Validation, token);
            await FailureAsync(admin, EventReferenceSelectProtocol.Operation(state.Partition, generation: 2),
                ErrorCode.TokenInvalidated, token);
            await EventReferenceSelectSources.VerifyAsync(admin, state, token);
            return true;
        }, token);

    private static async Task WithReaderAsync(ClusterFixture fixture, EventReferenceSelectState state,
        Func<RequestCqrsClusterCallers, Task> operation, CancellationToken token)
    {
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async callers => { await operation(callers); return true; }, token);
        await AdminAsync(fixture, state, token);
    }

    private static async Task FailureAsync(RequestCqrsClusterCallers callers, SqlOperationRequest request,
        ErrorCode code, CancellationToken token)
    {
        var result = await callers.Sdk.ExecuteSqlAsync(request, token);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
        _ = (await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token),
            code, dispatched: true)).Value;
    }
}
