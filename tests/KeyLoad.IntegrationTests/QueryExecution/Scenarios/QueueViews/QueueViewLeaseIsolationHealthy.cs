using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationHealthy
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, QueueViewLeaseIsolationState state, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Scenario.Partition,
            [new EnqueueMessage(SqlModelViewClusterScenario.Queue, QueueViewLeaseIsolationProtocol.Healthy,
                QueueViewLeaseIsolationProtocol.HealthyPayload, QueueViewLeaseIsolationProtocol.HealthyHeaders,
                SqlModelViewClusterScenario.NotBefore)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await QueueViewLeaseIsolationReceipts.AssertAsync(state.Scenario, receipt, command.CommandId,
            [new(QueueViewLeaseIsolationProtocol.EnqueueKind, SqlModelViewClusterScenario.Queue,
                QueueViewLeaseIsolationProtocol.Healthy, QueueViewLeaseIsolationProtocol.InitialVersion)]);
        state.Healthy = true;
        state.HealthyCommand = command;
        state.HealthyReceipt = receipt;
        var image = await QueueViewLeaseIsolationImages.ReadAsync(root, state, token);
        await SqlClusterProtocol.EqualAsync(new MessageInspection(new(QueueViewLeaseIsolationProtocol.Healthy, MessageState.Ready,
            QueueViewLeaseIsolationProtocol.Empty, QueueViewLeaseIsolationProtocol.InitialVersion,
            QueueViewLeaseIsolationProtocol.RenewedVersion, SqlModelViewClusterScenario.NotBefore, null),
            QueueViewLeaseIsolationProtocol.HealthyPayload, QueueViewLeaseIsolationProtocol.HealthyHeaders), image.Messages[^QueueViewLeaseIsolationProtocol.One]);
        state.Terminal = image;
    }
}
