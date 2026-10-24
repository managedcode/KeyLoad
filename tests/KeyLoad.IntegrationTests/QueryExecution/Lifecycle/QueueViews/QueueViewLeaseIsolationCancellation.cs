using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueueViewLeaseIsolationCancellation
{
    internal static Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        QueueViewLeaseIsolationState state, CancellationToken token)
        => QueueViewLeaseIsolationImages.ObserveAsync(root, state, async () =>
        {
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            await QueueViewLeaseIsolationProblems.SdkAsync(await reader.Sdk.QueryAsync(state.Scenario.QueueSql(), cancelled.Token), ErrorCode.Cancelled);
            await QueueViewLeaseIsolationProblems.SdkAsync(await reader.Sdk.QueryAstAsync(state.Scenario.QueueAst(), cancelled.Token), ErrorCode.Cancelled);
            var original = await Assert.ThrowsAsync<OperationCanceledException>(() => reader.Mcp.CallAsync(
                McpCallerTools.QueryExecute, state.Scenario.QueueSql(), cancelled.Token));
            await Assert.That(original?.CancellationToken == cancelled.Token).IsTrue();
            await QueueViewLeaseIsolationRows.AssertAsync(await QueueViewLeaseIsolationReads.ReadAsync(reader, state,
                QueueViewLeaseIsolationProtocol.Empty, false, token), state);
        }, token);
}
