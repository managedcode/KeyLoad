using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3Renewal
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueWorkerRf3Seed seed, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        QueueWorkerDeliveryContext? held = null;
        var revoked = MessagingRf3Identity.WithCapability(seed.Identity.Principal, seed.Lane,
            QueueWorkerRf3Protocol.Worker & ~Capability.QueueRenew);
        await MessagingRf3Identity.UpdateAsync(fixture, revoked, token);
        var policy = QueueWorkerRf3Protocol.Options(QueueWorkerRf3Protocol.SingleDelivery).Value with
        {
            RenewalInterval = TimeSpan.FromSeconds(QueueWorkerRf3Protocol.RenewalCadenceSeconds)
        };
        var result = await sdk.RunQueueAckWorkerAsync(seed.Lane, Guid.NewGuid(), async (context, handlerToken) =>
        {
            held = context;
            await QueueWorkerRf3Assertions.LeasedAsync(sdk, mcp, seed, context, QueueWorkerRf3Protocol.FirstReadySequence, handlerToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, handlerToken);
        }, Options.Create(policy), TimeProvider.System, token);
        ArgumentNullException.ThrowIfNull(held);
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.RenewalFailed);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerRf3Protocol.NoAcknowledgements);
        await Assert.That(result.Unacknowledged).HasSingleItem();
        await Assert.That(result.Operations.Any(row => row.Command?.Action == DeliveryAction.Ack)).IsFalse();
        var refused = await Assert.That(result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
            && row.Command?.Action == DeliveryAction.Renew && row.Completion is { IsFailed: true })).HasSingleItem();
        var completion = QueueWorkerRf3Completion.Require(refused);
        await Assert.That(completion.Value).IsNull();
        await Assert.That(refused.Command!.CommandId).IsEqualTo(held.FirstRenewalId);
        await Assert.That(completion.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await QueueWorkerRf3Assertions.LeasedAsync(sdk, mcp, seed, held, QueueWorkerRf3Protocol.FirstReadySequence, token);
        await QueueWorkerRf3Assertions.ReadyAsync(sdk, mcp, seed, QueueWorkerRf3Protocol.Second, QueueWorkerRf3Protocol.SecondReadySequence, token);
        await QueueWorkerRf3Assertions.ReadyAsync(sdk, mcp, seed, QueueWorkerRf3Protocol.Third, QueueWorkerRf3Protocol.ThirdReadySequence, token);
        var restored = MessagingRf3Identity.WithCapability(seed.Identity.Principal with
        {
            PolicyEpoch = seed.Identity.Principal.PolicyEpoch + QueueWorkerRf3Protocol.PolicyEpochIncrement
        }, seed.Lane, QueueWorkerRf3Protocol.Worker);
        await MessagingRf3Identity.UpdateAsync(fixture, restored, token);
        await QueueWorkerRf3RenewalContinuation.RunAsync(fixture, sdk, mcp, seed, held, token);
    }
}
