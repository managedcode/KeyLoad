using KeyLoad.Client;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3Process
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueWorkerRf3Seed seed, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        var failures = new List<Exception>();
        QueueWorkerRf3Child? child = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var input = new ExternalQueueWorkerCrashInput(http.BaseAddress!.AbsoluteUri, seed.Identity.Secret, seed.Lane,
                Guid.NewGuid(), seed.Identity.Principal.Id);
            child = await QueueWorkerRf3Child.StartAsync(input, token);
            await VerifyChildAsync(fixture, sdk, mcp, seed, child, input.ReceiveId, token);
        }, failures);
        if (child is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => child.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyChildAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerRf3Seed seed, QueueWorkerRf3Child child, Guid receiveId, CancellationToken token)
    {
        var claim = await child.WaitClaimAsync(token);
        var context = new QueueWorkerDeliveryContext(seed.Lane, claim.Delivery, claim.AcknowledgementId, claim.FirstRenewalId);
        await QueueWorkerRf3Assertions.LeasedAsync(sdk, mcp, seed, context, QueueWorkerRf3Protocol.FirstReadySequence, token);
        await child.KillAndJoinAsync();
        await ImagesAsync(sdk, mcp, seed, context, token);
        var request = new ReceiveRequest(receiveId, seed.Lane, QueueWorkerRf3Protocol.SingleDelivery,
            QueueWorkerRf3Protocol.Options(QueueWorkerRf3Protocol.SingleDelivery).Value.MaximumReceiveBytes,
            QueueWorkerRf3Protocol.LeaseSeconds);
        var recovered = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(request, token));
        await QueueWorkerRf3Assertions.EqualAsync(recovered.Deliveries.ToArray(), new[] { claim.Delivery });
        await Assert.That(recovered.RequestId).IsEqualTo(receiveId);
        await ImagesAsync(sdk, mcp, seed, context, token);
        var official = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(
            McpCallerTools.MessagesReceive, request, token));
        await QueueWorkerRf3Assertions.EqualAsync(official.Value, recovered);
        await ImagesAsync(sdk, mcp, seed, context, token);
        // Explicit parent completion follows a proved read-only callback, killed before ACK admission.
        var parentAck = new DeliveryCommand(claim.AcknowledgementId, seed.Lane, claim.Delivery.Token, DeliveryAction.Ack);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(parentAck, token));
        await QueueWorkerRf3Assertions.ReceiptAsync(receipt, parentAck.CommandId, seed,
            [new(nameof(DeliveryAction.Ack), seed.Lane.Queue, QueueWorkerRf3Protocol.First, QueueWorkerRf3Protocol.AckStateVersion)]);
        await QueueWorkerRf3Assertions.AckedAsync(sdk, mcp, seed, QueueWorkerRf3Protocol.First, QueueWorkerRf3Protocol.FirstReadySequence, token);
        await QueueWorkerRf3ProcessContinuation.RunAsync(fixture, sdk, mcp, seed, parentAck, receipt, token);
    }

    private static async Task ImagesAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerRf3Seed seed,
        QueueWorkerDeliveryContext context, CancellationToken token)
    {
        await QueueWorkerRf3Assertions.LeasedAsync(sdk, mcp, seed, context, QueueWorkerRf3Protocol.FirstReadySequence, token);
        await QueueWorkerRf3Assertions.ReadyAsync(sdk, mcp, seed, QueueWorkerRf3Protocol.Second, QueueWorkerRf3Protocol.SecondReadySequence, token);
        await QueueWorkerRf3Assertions.ReadyAsync(sdk, mcp, seed, QueueWorkerRf3Protocol.Third, QueueWorkerRf3Protocol.ThirdReadySequence, token);
    }
}
