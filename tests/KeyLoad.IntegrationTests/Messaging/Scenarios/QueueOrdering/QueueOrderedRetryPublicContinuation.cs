using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueOrderedRetryPublicContinuation
{
    internal static async Task RunAsync(RequestCqrsRf3Callers callers, QueueOrderedRetryPublicState state, CancellationToken token)
    {
        await QueueLifecyclePublicRoutes.CommitAsync(callers, state.Caller, new(Guid.NewGuid(), state.Caller.Partition,
            [new RedriveQueueMessage(state.Caller.Lane.Queue, QueueOrderedRetryPublicProtocol.First,
                QueueOrderedRetryPublicProtocol.Seven, QueueOrderedRetryPublicProtocol.One)]), token);
        await QueueLifecyclePublicAssertions.LiteralAsync(callers, state.Caller, QueueOrderedRetryPublicProtocol.First,
            new(QueueOrderedRetryPublicProtocol.First, MessageState.Ready, QueueOrderedRetryPublicProtocol.Initial,
                QueueOrderedRetryPublicProtocol.Eight, QueueOrderedRetryPublicProtocol.Four, null, null,
                LeaseVersion: QueueOrderedRetryPublicProtocol.Three, DeliveryGeneration: QueueOrderedRetryPublicProtocol.Two), true, token);
        await StaleAsync(callers, state, token);
        if (state.ParkedHead == QueueParkedHeadPolicy.Continue)
        {
            await Assert.That((await QueueOrderedRetryPublicOperations.ReceiveAsync(callers, state, token)).Deliveries).IsEmpty();
            await QueueOrderedRetryPublicOperations.CompleteAsync(callers, state, state.Second!, DeliveryAction.Ack, token);
        }
        var fresh = await QueueOrderedRetryPublicOperations.ClaimAsync(callers, state, QueueOrderedRetryPublicProtocol.First, token);
        await Assert.That(fresh.DeliveryGeneration).IsEqualTo(QueueOrderedRetryPublicProtocol.Two);
        await QueueOrderedRetryPublicOperations.CompleteAsync(callers, state, fresh, DeliveryAction.Ack, token);
        if (state.ParkedHead == QueueParkedHeadPolicy.Block)
        {
            var second = await QueueOrderedRetryPublicOperations.ClaimAsync(callers, state, QueueOrderedRetryPublicProtocol.Second, token);
            await QueueOrderedRetryPublicOperations.CompleteAsync(callers, state, second, DeliveryAction.Ack, token);
        }
        await QueueOrderedRetryPublicTerminal.RunAsync(callers, state, healthy: false, token);
        await QueueLifecyclePublicRoutes.ReplayAsync(callers, state.Caller, token);
    }
    private static async Task StaleAsync(RequestCqrsRf3Callers callers, QueueOrderedRetryPublicState state, CancellationToken token)
    {
        var old = new DeliveryCommand(Guid.NewGuid(), state.Caller.Lane, state.Original!.Token, DeliveryAction.Ack);
        await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(await callers.Sdk.CompleteAsync(old, token), ErrorCode.StaleLease);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.MessagesComplete, old, token), ErrorCode.StaleLease, dispatched: true);
        var sql = SqlRf3Protocol.Call(state.Caller.Partition, McpCallerTools.MessagesComplete, old);
        await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(await callers.Sdk.ExecuteSqlAsync(sql, token), ErrorCode.StaleLease);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), ErrorCode.StaleLease, dispatched: true);
        await QueueLifecyclePublicAssertions.LiteralAsync(callers, state.Caller, QueueOrderedRetryPublicProtocol.First,
            new(QueueOrderedRetryPublicProtocol.First, MessageState.Ready, QueueOrderedRetryPublicProtocol.Initial,
                QueueOrderedRetryPublicProtocol.Eight, QueueOrderedRetryPublicProtocol.Four, null, null,
                LeaseVersion: QueueOrderedRetryPublicProtocol.Three, DeliveryGeneration: QueueOrderedRetryPublicProtocol.Two), true, token);
    }
}
