using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class QueueReferenceSelectFlow
{
    internal static Task ReaderAsync(ClusterFixture fixture, QueueReferenceSelectState state,
        bool denied, bool masked, CancellationToken token)
        => WithReaderAsync(fixture, state, async callers =>
        {
            if (denied)
            {
                await FailureAsync(callers, QueueReferenceSelectProtocol.Operation(state.Partition), ErrorCode.PermissionDenied, token);
                await FailureAsync(callers, QueueReferenceSelectProtocol.Operation(state.Partition, QueueReferenceSelectProtocol.EmptyQueue), ErrorCode.PermissionDenied, token);
            }
            else
            {
                await QueueReferenceSelectAssertions.PageAsync(callers, state, masked, token);
                var empty = await SqlClusterProtocol.SdkAsync<QueryPage>(callers.Sdk,
                    QueueReferenceSelectProtocol.Operation(state.Partition, QueueReferenceSelectProtocol.EmptyQueue), token);
                await Assert.That(empty.Rows).IsEmpty();
                await Assert.That(empty.Cursor).IsNull();
                await EventReferenceSelectAssertions.SamePageAsync(empty,
                    await SqlClusterProtocol.McpAsync<QueryPage>(callers.Mcp,
                        QueueReferenceSelectProtocol.Operation(state.Partition, QueueReferenceSelectProtocol.EmptyQueue), token));
                await FailureAsync(callers, QueueReferenceSelectProtocol.Operation(state.Partition) with
                {
                    Parameters = null
                }, ErrorCode.Validation, token);
                await QueueReferenceSelectAssertions.PageAsync(callers, state, masked, token);
            }
        }, token);

    internal static Task AdminAsync(ClusterFixture fixture, QueueReferenceSelectState state, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            await FailureAsync(admin, QueueReferenceSelectProtocol.Operation(state.Partition, QueueReferenceSelectProtocol.InvalidQueue),
                ErrorCode.Validation, token);
            await QueueReferenceSelectSources.VerifyAsync(admin, state, token);
            return true;
        }, token);

    private static async Task WithReaderAsync(ClusterFixture fixture, QueueReferenceSelectState state,
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
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
        _ = (await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token),
            code, dispatched: true)).Value;
    }
}
