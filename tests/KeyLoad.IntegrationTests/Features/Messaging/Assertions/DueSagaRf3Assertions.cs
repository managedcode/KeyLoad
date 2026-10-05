using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Observes automatic timeout state, single queue output and cross-client ACK parity.</summary>
internal static class DueSagaRf3Assertions
{
    private const long InitialRevision = 1;
    private const string TimeoutPrefix = "saga-timeout-";
    private const string GuidFormat = "N";
    private const string RevisionFormat = "x16";
    internal const int TimeoutSeconds = 60;
    private static readonly TimeSpan ProgressWindow = TimeSpan.FromSeconds(70);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    internal static async Task WaitForTimedOutAsync(KeyLoadClient sdk, MessagingRf3Scenario scenario, Guid sagaId,
        CancellationToken cancellationToken)
    {
        var until = TimeProvider.System.GetUtcNow() + ProgressWindow;
        while (TimeProvider.System.GetUtcNow() < until)
        {
            var view = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectSagaAsync(
                new(scenario.SourceQueue, sagaId), cancellationToken))
                ?? throw new InvalidOperationException("The persisted waiting saga disappeared before timeout.");
            if (view.Phase == SagaPhase.TimedOut)
            {
                return;
            }
            if (view.Phase != SagaPhase.Waiting)
            {
                throw new InvalidOperationException("The saga left Waiting without its autonomous timeout transition.");
            }
            await Task.Delay(PollInterval, cancellationToken);
        }
        throw new TimeoutException("The waiting saga did not time out within its bounded RF3 window.");
    }

    internal static async Task AssertSagaAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid sagaId, SagaPhase expectedPhase, long expectedRevision,
        DateTimeOffset expectedDeadline, string credential, CancellationToken cancellationToken)
    {
        var request = new InspectSagaRequest(scenario.SourceQueue, sagaId);
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectSagaAsync(request, cancellationToken));
        var reply = await mcp.CallAsync(McpCallerTools.SagaInspect, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<SagaInspection?>(reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(mcpView.Value).IsNotNull();
        await Assert.That(sdkView!.Lane).IsEqualTo(scenario.SourceQueue);
        await Assert.That(sdkView.SagaId).IsEqualTo(sagaId);
        await Assert.That(sdkView.Phase).IsEqualTo(expectedPhase);
        await Assert.That(sdkView.Revision).IsEqualTo(expectedRevision);
        await Assert.That(sdkView.Deadline).IsEqualTo(expectedDeadline);
        await Assert.That(sdkView.StateJson).IsEqualTo("{}");
        await Assert.That(sdkView.Redacted).IsTrue();
        await Assert.That(JsonDefaults.Serialize(mcpView.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(sdkView))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.SagaTimeoutPayload);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedHeaders);
    }

    internal static async Task AssertTimeoutAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid sagaId, DateTimeOffset dueAt, string credential,
        CancellationToken cancellationToken)
    {
        var id = TimeoutId(sagaId, InitialRevision);
        var request = new InspectMessageRequest(scenario.TimeoutQueue, id);
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        var reply = await mcp.CallAsync(McpCallerTools.MessagesInspect, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<MessageInspection?>(reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(mcpView.Value).IsNotNull();
        await Assert.That(sdkView!.Metadata.Id).IsEqualTo(id);
        await Assert.That(sdkView.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(sdkView.Metadata.NotBefore).IsNull();
        await Assert.That(sdkView.Metadata.ExpiresAt).IsEqualTo(dueAt.AddSeconds(TimeoutSeconds));
        await Assert.That(JsonDefaults.Serialize(mcpView.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(sdkView))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.SagaTimeoutPayload);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedHeaders);
    }

    internal static async Task AssertTimeoutAbsentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid sagaId, CancellationToken cancellationToken)
    {
        var request = new InspectMessageRequest(scenario.TimeoutQueue, TimeoutId(sagaId, InitialRevision));
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        var reply = await mcp.CallAsync(McpCallerTools.MessagesInspect, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<MessageInspection?>(reply);
        await Assert.That(sdkView).IsNull();
        await Assert.That(mcpView.Value).IsNull();
    }

    internal static async Task ConsumeAndAcknowledgeExactlyOneAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid sagaId, string credential, CancellationToken cancellationToken)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), scenario.TimeoutQueue, MaxMessages: 2);
        var receiveReply = await mcp.CallAsync(McpCallerTools.MessagesReceive, request, cancellationToken);
        var received = await McpCallerAssertions.SuccessAsync<ReceiveResult>(receiveReply);
        await Assert.That(received.Value.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(received.Value.Deliveries).HasSingleItem();
        var delivery = received.Value.Deliveries[0];
        await Assert.That(delivery.Id).IsEqualTo(TimeoutId(sagaId, InitialRevision));
        await Assert.That(delivery.PayloadJson).IsEqualTo("{}");
        await Assert.That(delivery.HeadersJson).IsEqualTo("{}");
        await McpCallerAssertions.DoesNotDiscloseAsync(receiveReply, credential, MessagingRf3Scenario.SagaTimeoutPayload);
        await McpCallerAssertions.DoesNotDiscloseAsync(receiveReply, credential, MessagingRf3Scenario.ProtectedHeaders);

        var ack = new DeliveryCommand(Guid.NewGuid(), scenario.TimeoutQueue, delivery.Token, DeliveryAction.Ack);
        var mcpReceipt = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.MessagesComplete, ack, cancellationToken));
        var sdkReceipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(ack, cancellationToken));
        await Assert.That(JsonDefaults.Serialize(sdkReceipt).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(mcpReceipt.Value))).IsTrue();
        await Assert.That(sdkReceipt.Mutations).HasSingleItem();

        await AssertEmptyReceiveAsync(sdk, scenario, cancellationToken);
        await AssertEmptyReceiveAsync(mcp, scenario, cancellationToken);
        await AssertAcknowledgedAsync(sdk, mcp, scenario, delivery.Id, cancellationToken);
    }

    private static async Task AssertEmptyReceiveAsync(KeyLoadClient sdk, MessagingRf3Scenario scenario,
        CancellationToken cancellationToken)
    {
        var empty = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), scenario.TimeoutQueue, MaxMessages: 2), cancellationToken));
        await Assert.That(empty.Deliveries).IsEmpty();
    }

    private static async Task AssertEmptyReceiveAsync(McpOfficialClient mcp, MessagingRf3Scenario scenario,
        CancellationToken cancellationToken)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), scenario.TimeoutQueue, MaxMessages: 2);
        var reply = await mcp.CallAsync(McpCallerTools.MessagesReceive, request, cancellationToken);
        var empty = await McpCallerAssertions.SuccessAsync<ReceiveResult>(reply);
        await Assert.That(empty.Value.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(empty.Value.Deliveries).IsEmpty();
    }

    private static async Task AssertAcknowledgedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, string id, CancellationToken cancellationToken)
    {
        var request = new InspectMessageRequest(scenario.TimeoutQueue, id);
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, cancellationToken));
        var reply = await mcp.CallAsync(McpCallerTools.MessagesInspect, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<MessageInspection?>(reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(sdkView!.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(mcpView.Value).IsNotNull();
        await Assert.That(JsonDefaults.Serialize(mcpView.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(sdkView))).IsTrue();
    }

    private static string TimeoutId(Guid sagaId, long waitingRevision)
        => string.Concat(TimeoutPrefix, sagaId.ToString(GuidFormat), "-",
            waitingRevision.ToString(RevisionFormat, System.Globalization.CultureInfo.InvariantCulture));
}
