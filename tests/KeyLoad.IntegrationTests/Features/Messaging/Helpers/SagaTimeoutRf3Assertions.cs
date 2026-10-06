using System.Globalization;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SagaTimeoutRf3Assertions
{
    private const string InspectTool = "keyload_saga_inspect";
    private static readonly TimeSpan ProgressWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    internal static async Task AssertProjectedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid sagaId, DateTimeOffset expectedDeadline, string credential,
        CancellationToken cancellationToken)
    {
        var request = new InspectSagaRequest(scenario.SourceQueue, sagaId);
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectSagaAsync(request, cancellationToken));
        var reply = await mcp.CallAsync(InspectTool, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<SagaInspection?>(reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(mcpView.Value).IsNotNull();
        await Assert.That(sdkView!.Phase).IsEqualTo(SagaPhase.Waiting);
        await Assert.That(sdkView.StateJson).IsEqualTo("{}");
        await Assert.That(sdkView.Redacted).IsTrue();
        await Assert.That(sdkView.Deadline).IsNotNull();
        await Assert.That(sdkView.Deadline).IsEqualTo(expectedDeadline);
        await Assert.That(JsonDefaults.Serialize(mcpView.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(sdkView))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.SagaTimeoutPayload);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedHeaders);
    }

    internal static async Task WaitUntilAsync(DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        var remaining = dueAt - TimeProvider.System.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, TimeProvider.System, cancellationToken);
        }
    }

    internal static async Task WaitForTimedOutAsync(KeyLoadClient sdk, MessagingRf3Scenario scenario,
        Guid sagaId, CancellationToken cancellationToken)
    {
        var until = TimeProvider.System.GetUtcNow() + ProgressWindow;
        while (TimeProvider.System.GetUtcNow() < until)
        {
            var view = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectSagaAsync(
                new(scenario.SourceQueue, sagaId), cancellationToken))
                ?? throw new InvalidOperationException("The persisted waiting saga disappeared before timeout.");
            if (view.Phase == SagaPhase.TimedOut && view.Revision == 2)
            {
                return;
            }
            if (view.Phase != SagaPhase.Waiting || view.Revision != 1)
            {
                throw new InvalidOperationException("The saga left Waiting without its autonomous terminal transition.");
            }
            await Task.Delay(PollInterval, TimeProvider.System, cancellationToken);
        }
        throw new TimeoutException("The autonomous timeout did not commit within its bounded RF3 window.");
    }

    internal static async Task AssertPhaseAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid sagaId, SagaPhase phase, long expectedRevision,
        DateTimeOffset expectedDeadline,
        CancellationToken cancellationToken)
    {
        var request = new InspectSagaRequest(scenario.SourceQueue, sagaId);
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectSagaAsync(request, cancellationToken));
        var mcpView = await McpCallerAssertions.SuccessAsync<SagaInspection?>(
            await mcp.CallAsync(InspectTool, request, cancellationToken));
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(mcpView.Value).IsNotNull();
        await Assert.That(sdkView!.Phase).IsEqualTo(phase);
        await Assert.That(sdkView.Revision).IsEqualTo(expectedRevision);
        await Assert.That(sdkView.Deadline).IsEqualTo(expectedDeadline);
        await Assert.That(JsonDefaults.Serialize(mcpView.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(sdkView))).IsTrue();
    }

    internal static async Task AssertTimeoutMessageAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid sagaId, DateTimeOffset dueAt, string credential,
        CancellationToken cancellationToken)
    {
        var request = new InspectMessageRequest(scenario.TimeoutQueue, TimeoutMessageId(sagaId));
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        var reply = await mcp.CallAsync(McpCallerTools.MessagesInspect, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<MessageInspection?>(reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(mcpView.Value).IsNotNull();
        await Assert.That(sdkView!.Metadata.Id).IsEqualTo(TimeoutMessageId(sagaId));
        await Assert.That(sdkView.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(sdkView.Metadata.NotBefore).IsNull();
        await Assert.That(sdkView.Metadata.ExpiresAt).IsEqualTo(dueAt.AddMinutes(1));
        await Assert.That(sdkView.PayloadJson).IsEqualTo("{}");
        await Assert.That(sdkView.HeadersJson).IsEqualTo("{}");
        await Assert.That(JsonDefaults.Serialize(mcpView.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(sdkView))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.SagaTimeoutPayload);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedHeaders);
    }

    internal static async Task AcknowledgeSingleTimeoutAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid sagaId, string credential, CancellationToken cancellationToken)
    {
        var delivery = await ReceiveSingleTimeoutAsync(mcp, scenario, sagaId, credential, cancellationToken);
        var ack = new DeliveryCommand(Guid.NewGuid(), scenario.TimeoutQueue, delivery.Token, DeliveryAction.Ack);
        var mcpReceipt = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.MessagesComplete, ack, cancellationToken));
        var sdkReceipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(ack, cancellationToken));
        await Assert.That(JsonDefaults.Serialize(mcpReceipt.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(sdkReceipt))).IsTrue();
        await AssertEmptyReceiveAsync(sdk, mcp, scenario, cancellationToken);
    }

    private static async Task<Delivery> ReceiveSingleTimeoutAsync(McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid sagaId, string credential, CancellationToken cancellationToken)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), scenario.TimeoutQueue, MaxMessages: 2);
        var reply = await mcp.CallAsync(McpCallerTools.MessagesReceive, request, cancellationToken);
        var received = await McpCallerAssertions.SuccessAsync<ReceiveResult>(reply);
        await Assert.That(received.Value.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(received.Value.Deliveries).HasSingleItem();
        var delivery = received.Value.Deliveries[0];
        await Assert.That(delivery.Id).IsEqualTo(TimeoutMessageId(sagaId));
        await Assert.That(delivery.PayloadJson).IsEqualTo("{}");
        await Assert.That(delivery.HeadersJson).IsEqualTo("{}");
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.SagaTimeoutPayload);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedHeaders);
        return delivery;
    }

    private static async Task AssertEmptyReceiveAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var sdkResult = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), scenario.TimeoutQueue, MaxMessages: 2), cancellationToken));
        await Assert.That(sdkResult.Deliveries).IsEmpty();
        var request = new ReceiveRequest(Guid.NewGuid(), scenario.TimeoutQueue, MaxMessages: 2);
        var reply = await mcp.CallAsync(McpCallerTools.MessagesReceive, request, cancellationToken);
        var mcpResult = await McpCallerAssertions.SuccessAsync<ReceiveResult>(reply);
        await Assert.That(mcpResult.Value.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(mcpResult.Value.Deliveries).IsEmpty();
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
