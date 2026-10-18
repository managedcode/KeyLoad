using KeyLoad.Client;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerClusterProcess
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueWorkerClusterSeed seed, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        var failures = new List<Exception>();
        QueueWorkerClusterChild? child = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var input = new ExternalQueueWorkerCrashInput(http.BaseAddress!.AbsoluteUri, seed.Identity.Secret, seed.Lane,
                Guid.NewGuid(), seed.Identity.Principal.Id);
            child = await QueueWorkerClusterChild.StartAsync(input, token);
            await VerifyChildAsync(fixture, sdk, mcp, seed, child, input.ReceiveId, token);
        }, failures);
        if (child is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => child.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyChildAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerClusterSeed seed, QueueWorkerClusterChild child, Guid receiveId, CancellationToken token)
    {
        var claim = await child.WaitClaimAsync(token);
        var context = new QueueWorkerDeliveryContext(seed.Lane, claim.Delivery, claim.AcknowledgementId, claim.FirstRenewalId);
        await QueueWorkerClusterAssertions.LeasedAsync(sdk, mcp, seed, context, QueueWorkerClusterProtocol.FirstReadySequence, token);
        await child.KillAndJoinAsync();
        await ImagesAsync(sdk, mcp, seed, context, token);
        var request = new ReceiveRequest(receiveId, seed.Lane, QueueWorkerClusterProtocol.SingleDelivery,
            QueueWorkerClusterProtocol.Options(QueueWorkerClusterProtocol.SingleDelivery).Value.MaximumReceiveBytes,
            QueueWorkerClusterProtocol.LeaseSeconds);
        var recovered = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(request, token));
        await QueueWorkerClusterAssertions.EqualAsync(recovered.Deliveries.ToArray(), new[] { claim.Delivery });
        await Assert.That(recovered.RequestId).IsEqualTo(receiveId);
        await ImagesAsync(sdk, mcp, seed, context, token);
        var official = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(
            McpCallerTools.MessagesReceive, request, token));
        await QueueWorkerClusterAssertions.EqualAsync(official.Value, recovered);
        await ImagesAsync(sdk, mcp, seed, context, token);
        // Explicit parent completion follows a proved read-only callback, killed before ACK admission.
        var parentAck = new DeliveryCommand(claim.AcknowledgementId, seed.Lane, claim.Delivery.Token, DeliveryAction.Ack);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(parentAck, token));
        await QueueWorkerClusterAssertions.ReceiptAsync(receipt, parentAck.CommandId, seed,
            [new(nameof(DeliveryAction.Ack), seed.Lane.Queue, QueueWorkerClusterProtocol.First, QueueWorkerClusterProtocol.AckStateVersion)]);
        await QueueWorkerClusterAssertions.AckedAsync(sdk, mcp, seed, QueueWorkerClusterProtocol.First, QueueWorkerClusterProtocol.FirstReadySequence, token);
        await QueueWorkerClusterProcessContinuation.RunAsync(fixture, sdk, mcp, seed, parentAck, receipt, token);
    }

    private static async Task ImagesAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerClusterSeed seed,
        QueueWorkerDeliveryContext context, CancellationToken token)
    {
        await QueueWorkerClusterAssertions.LeasedAsync(sdk, mcp, seed, context, QueueWorkerClusterProtocol.FirstReadySequence, token);
        await QueueWorkerClusterAssertions.ReadyAsync(sdk, mcp, seed, QueueWorkerClusterProtocol.Second, QueueWorkerClusterProtocol.SecondReadySequence, token);
        await QueueWorkerClusterAssertions.ReadyAsync(sdk, mcp, seed, QueueWorkerClusterProtocol.Third, QueueWorkerClusterProtocol.ThirdReadySequence, token);
    }
}
