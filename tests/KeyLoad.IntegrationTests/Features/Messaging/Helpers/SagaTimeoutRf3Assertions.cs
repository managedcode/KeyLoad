using System.Globalization;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SagaTimeoutRf3Assertions
{
    private const string InspectTool = "keyload_saga_inspect";

    internal static async Task AssertProjectedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid sagaId, string credential, CancellationToken cancellationToken)
    {
        var request = new InspectSagaRequest(scenario.SourceQueue, sagaId);
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectSagaAsync(request, cancellationToken));
        var reply = await mcp.CallAsync(InspectTool, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<SagaInspection?>(reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(sdkView!.Phase).IsEqualTo(SagaPhase.Waiting);
        await Assert.That(sdkView.StateJson).IsEqualTo("{}");
        await Assert.That(sdkView.Redacted).IsTrue();
        await Assert.That(sdkView.Deadline).IsNotNull();
        await Assert.That(JsonDefaults.Serialize(mcpView.Value)).IsEqualTo(JsonDefaults.Serialize(sdkView));
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.SagaTimeoutPayload);
    }

    internal static async Task WaitUntilAsync(DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        var remaining = dueAt - TimeProvider.System.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken);
        }
    }

    internal static async Task<CommandRequest> ExpireAtDeadlineAsync(McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid sagaId, DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        var until = dueAt.AddSeconds(10);
        while (TimeProvider.System.GetUtcNow() < until)
        {
            var request = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
                new ExpireSaga(scenario.SourceQueue, sagaId, 1));
            var reply = await mcp.CallAsync(McpCallerTools.DocumentsCommit, request, cancellationToken);
            if (reply.IsError is not true)
            {
                _ = await McpCallerAssertions.SuccessAsync<CommitReceipt>(reply);
                return request;
            }
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Validation, dispatched: true);
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
        throw new TimeoutException("The logged saga deadline did not become due within its bounded RF3 window.");
    }

    internal static async Task AssertPhaseAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid sagaId, SagaPhase phase, long expectedRevision,
        CancellationToken cancellationToken)
    {
        var request = new InspectSagaRequest(scenario.SourceQueue, sagaId);
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectSagaAsync(request, cancellationToken));
        var mcpView = await McpCallerAssertions.SuccessAsync<SagaInspection?>(
            await mcp.CallAsync(InspectTool, request, cancellationToken));
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(sdkView!.Phase).IsEqualTo(phase);
        await Assert.That(sdkView.Revision).IsEqualTo(expectedRevision);
        await Assert.That(mcpView.Value).IsEqualTo(sdkView);
    }

    internal static async Task AssertTimeoutMessageAsync(KeyLoadClient sdk, MessagingRf3Scenario scenario,
        Guid sagaId, DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        var message = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            new(scenario.TimeoutQueue, TimeoutMessageId(sagaId)), cancellationToken));
        await Assert.That(message).IsNotNull();
        await Assert.That(message!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(message.Metadata.ExpiresAt).IsEqualTo(dueAt.AddMinutes(1));
        await Assert.That(message.PayloadJson).IsEqualTo("{}");
        await Assert.That(message.HeadersJson).IsEqualTo("{}");
    }

    internal static async Task AcknowledgeSingleTimeoutAsync(KeyLoadClient sdk, MessagingRf3Scenario scenario,
        Guid sagaId, CancellationToken cancellationToken)
    {
        var received = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), scenario.TimeoutQueue, MaxMessages: 2), cancellationToken));
        await Assert.That(received.Deliveries).HasSingleItem();
        var delivery = received.Deliveries[0];
        await Assert.That(delivery.Id).IsEqualTo(TimeoutMessageId(sagaId));
        await Assert.That(delivery.PayloadJson).IsEqualTo(MessagingRf3Scenario.SagaTimeoutPayload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(MessagingRf3Scenario.ProtectedHeaders);
        var ack = new DeliveryCommand(Guid.NewGuid(), scenario.TimeoutQueue, delivery.Token, DeliveryAction.Ack);
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(ack, cancellationToken));
        var next = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), scenario.TimeoutQueue, MaxMessages: 2), cancellationToken));
        await Assert.That(next.Deliveries).IsEmpty();
    }

    internal static async Task AssertNoSecondTransitionAsync(KeyLoadClient sdk, MessagingRf3Scenario scenario,
        Guid sagaId, CancellationToken cancellationToken)
    {
        var request = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new ExpireSaga(scenario.SourceQueue, sagaId, 1));
        await AssertSdkErrorAsync(await sdk.CommitAsync(request, cancellationToken), ErrorCode.RevisionConflict);
        var message = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            new(scenario.TimeoutQueue, TimeoutMessageId(sagaId)), cancellationToken));
        await Assert.That(message!.Metadata.State).IsEqualTo(MessageState.Acked);
        var empty = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), scenario.TimeoutQueue), cancellationToken));
        await Assert.That(empty.Deliveries).IsEmpty();
    }

    internal static async Task AssertSdkErrorAsync<T>(ManagedCode.Communication.Result<T> result,
        ErrorCode expected)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(expected.ToString());
    }

    private static string TimeoutMessageId(Guid sagaId)
        => string.Concat("saga-timeout-", sagaId.ToString("N"), "-",
            1L.ToString("x16", CultureInfo.InvariantCulture));
}
