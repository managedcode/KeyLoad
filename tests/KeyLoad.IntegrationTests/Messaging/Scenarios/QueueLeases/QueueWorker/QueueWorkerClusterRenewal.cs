using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueWorkerClusterRenewal
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueWorkerClusterSeed seed, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        QueueWorkerDeliveryContext? held = null;
        var revoked = MessagingClusterIdentity.WithCapability(seed.Identity.Principal, seed.Lane,
            QueueWorkerClusterProtocol.Worker & ~Capability.QueueRenew);
        await MessagingClusterIdentity.UpdateAsync(fixture, revoked, token);
        var policy = QueueWorkerClusterProtocol.Options(QueueWorkerClusterProtocol.SingleDelivery).Value with
        {
            RenewalInterval = TimeSpan.FromSeconds(QueueWorkerClusterProtocol.RenewalCadenceSeconds)
        };
        var result = await sdk.RunQueueAckWorkerAsync(seed.Lane, Guid.NewGuid(), async (context, handlerToken) =>
        {
            held = context;
            await QueueWorkerClusterAssertions.LeasedAsync(sdk, mcp, seed, context, QueueWorkerClusterProtocol.FirstReadySequence, handlerToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, handlerToken);
        }, Options.Create(policy), TimeProvider.System, token);
        ArgumentNullException.ThrowIfNull(held);
        await Assert.That(result.Reason).IsEqualTo(QueueWorkerStopReason.RenewalFailed);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerClusterProtocol.NoAcknowledgements);
        await Assert.That(result.Unacknowledged).HasSingleItem();
        await Assert.That(result.Operations.Any(row => row.Command?.Action == DeliveryAction.Ack)).IsFalse();
        var refused = await Assert.That(result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
            && row.Command?.Action == DeliveryAction.Renew && row.Completion is { IsFailed: true })).HasSingleItem();
        var completion = QueueWorkerClusterCompletion.Require(refused);
        await Assert.That(completion.Value).IsNull();
        await Assert.That(refused.Command!.CommandId).IsEqualTo(held.FirstRenewalId);
        await Assert.That(completion.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await QueueWorkerClusterAssertions.LeasedAsync(sdk, mcp, seed, held, QueueWorkerClusterProtocol.FirstReadySequence, token);
        await QueueWorkerClusterAssertions.ReadyAsync(sdk, mcp, seed, QueueWorkerClusterProtocol.Second, QueueWorkerClusterProtocol.SecondReadySequence, token);
        await QueueWorkerClusterAssertions.ReadyAsync(sdk, mcp, seed, QueueWorkerClusterProtocol.Third, QueueWorkerClusterProtocol.ThirdReadySequence, token);
        var restored = MessagingClusterIdentity.WithCapability(seed.Identity.Principal with
        {
            PolicyEpoch = seed.Identity.Principal.PolicyEpoch + QueueWorkerClusterProtocol.PolicyEpochIncrement
        }, seed.Lane, QueueWorkerClusterProtocol.Worker);
        await MessagingClusterIdentity.UpdateAsync(fixture, restored, token);
        await QueueWorkerClusterRenewalContinuation.RunAsync(fixture, sdk, mcp, seed, held, token);
    }
}
