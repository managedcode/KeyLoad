using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

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
                var empty = await SqlRf3Protocol.SdkAsync<QueryPage>(callers.Sdk,
                    QueueReferenceSelectProtocol.Operation(state.Partition, QueueReferenceSelectProtocol.EmptyQueue), token);
                await Assert.That(empty.Rows).IsEmpty();
                await Assert.That(empty.Cursor).IsNull();
                await RelationalSqlRf3JoinPageAssertions.SameRowsAsync(empty,
                    await SqlRf3Protocol.McpAsync<QueryPage>(callers.Mcp,
                        QueueReferenceSelectProtocol.Operation(state.Partition, QueueReferenceSelectProtocol.EmptyQueue), token));
                await FailureAsync(callers, QueueReferenceSelectProtocol.Operation(state.Partition) with
                {
                    Parameters = null
                }, ErrorCode.Validation, token);
                await QueueReferenceSelectAssertions.PageAsync(callers, state, masked, token);
            }
        }, token);

    internal static Task AdminAsync(ClusterFixture fixture, QueueReferenceSelectState state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            await FailureAsync(admin, QueueReferenceSelectProtocol.Operation(state.Partition, QueueReferenceSelectProtocol.InvalidQueue),
                ErrorCode.Validation, token);
            await QueueReferenceSelectSources.VerifyAsync(admin, state, token);
            return true;
        }, token);

    private static async Task WithReaderAsync(ClusterFixture fixture, QueueReferenceSelectState state,
        Func<RequestCqrsRf3Callers, Task> operation, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async callers => { await operation(callers); return true; }, token);
        await AdminAsync(fixture, state, token);
    }

    private static async Task FailureAsync(RequestCqrsRf3Callers callers, SqlOperationRequest request,
        ErrorCode code, CancellationToken token)
    {
        var result = await callers.Sdk.ExecuteSqlAsync(request, token);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token),
            code, dispatched: true);
    }
}
