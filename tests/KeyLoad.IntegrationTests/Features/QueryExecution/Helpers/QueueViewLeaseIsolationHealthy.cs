using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationHealthy
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, QueueViewLeaseIsolationState state, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Scenario.Partition,
            [new EnqueueMessage(SqlModelViewRf3Scenario.Queue, QueueViewLeaseIsolationProtocol.Healthy,
                QueueViewLeaseIsolationProtocol.HealthyPayload, QueueViewLeaseIsolationProtocol.HealthyHeaders,
                SqlModelViewRf3Scenario.NotBefore)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await QueueViewLeaseIsolationReceipts.AssertAsync(root, state.Scenario, receipt, command.CommandId,
            [new(QueueViewLeaseIsolationProtocol.EnqueueKind, SqlModelViewRf3Scenario.Queue,
                QueueViewLeaseIsolationProtocol.Healthy, QueueViewLeaseIsolationProtocol.InitialVersion)], token);
        state.Healthy = true;
        state.HealthyCommand = command;
        state.HealthyReceipt = receipt;
        var image = await QueueViewLeaseIsolationImages.ReadAsync(root, state, token);
        await SqlRf3Protocol.EqualAsync(new MessageInspection(new(QueueViewLeaseIsolationProtocol.Healthy, MessageState.Ready,
            QueueViewLeaseIsolationProtocol.Empty, QueueViewLeaseIsolationProtocol.InitialVersion,
            QueueViewLeaseIsolationProtocol.RenewedVersion, SqlModelViewRf3Scenario.NotBefore, null),
            QueueViewLeaseIsolationProtocol.HealthyPayload, QueueViewLeaseIsolationProtocol.HealthyHeaders), image.Messages[^QueueViewLeaseIsolationProtocol.One]);
        state.Terminal = image;
    }
}
