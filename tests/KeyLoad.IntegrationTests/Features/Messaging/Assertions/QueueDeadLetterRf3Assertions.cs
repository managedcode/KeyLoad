using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadLetterRf3Assertions
{
    private const string PrivateCompositionReferencesProperty = "compositionReferences";
    internal static async Task DeliveryAsync(Delivery actual, string id)
    {
        await Assert.That(actual.Id).IsEqualTo(id);
        await Assert.That(actual.PayloadJson).IsEqualTo(QueueDeadLetterRf3Protocol.Payload);
        await Assert.That(actual.HeadersJson).IsEqualTo(QueueDeadLetterRf3Protocol.Headers);
        await Assert.That(actual.Attempt).IsEqualTo(QueueDeadLetterRf3Protocol.One);
        await Assert.That(actual.LeaseVersion).IsEqualTo(QueueDeadLetterRf3Protocol.First);
        await Assert.That(actual.DeliveryGeneration).IsEqualTo(QueueDeadLetterRf3Protocol.First);
        await Assert.That(actual.Token).IsNotEmpty();
    }

    internal static async Task LiteralAsync(RequestCqrsRf3Callers callers, QueueDeadLetterRf3State state, CancellationToken token)
    {
        var request = new InspectMessageRequest(state.Lane, QueueDeadLetterRf3Protocol.Message);
        var expected = new MessageInspection(new(QueueDeadLetterRf3Protocol.Message, MessageState.DeadLettered,
            QueueDeadLetterRf3Protocol.One, QueueDeadLetterRf3Protocol.TerminalVersion, QueueDeadLetterRf3Protocol.First,
            null, null, LeaseVersion: QueueDeadLetterRf3Protocol.First, SafeFailureCode: QueueDeadLetterRf3Protocol.Exhausted, ParkedSequence: QueueDeadLetterRf3Protocol.First),
            QueueDeadLetterRf3Protocol.Payload, QueueDeadLetterRf3Protocol.Headers);
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.InspectAsync(request, token)), expected);
        await EqualAsync((await McpCallerAssertions.SuccessAsync<MessageInspection>(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesInspect, request, token))).Value, expected);
    }

    internal static async Task ReceiptAsync(RequestCqrsRf3Callers callers, CommandRequest request,
        CommitReceipt original, CancellationToken token)
    {
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(request, token)), original);
        await EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, request, token))).Value, original);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers callers, QueueDeadLetterRf3State state, CancellationToken token)
    {
        await ReceiptAsync(callers, state.Producer, state.Produced, token);
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteAsync(state.Nack, token)), state.Completed);
        await EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesComplete, state.Nack, token))).Value, state.Completed);
        await RefusalAsync(callers, state, token);
        await LiteralAsync(callers, state, token);
    }

    internal static async Task RefusalAsync(RequestCqrsRf3Callers callers, QueueDeadLetterRf3State state, CancellationToken token)
    {
        await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(await callers.Sdk.CommitAsync(state.Refused, token), ErrorCode.ResourceExhausted);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
            state.Refused, token), ErrorCode.ResourceExhausted, dispatched: true);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.GetAsync(
            new(state.Partition, QueueDeadLetterRf3Protocol.Collection, QueueDeadLetterRf3Protocol.Refused), token))).IsNull();
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.InspectAsync(
            new(state.Lane, QueueDeadLetterRf3Protocol.Refused), token))).IsNull();
    }

    internal static async Task DeniedAsync(ClusterFixture fixture, QueueDeadLetterRf3State state, CancellationToken token)
    {
        var identity = await MessagingRf3Identity.CreateAsync(fixture, state.Partition.TenantId,
            [new(state.Partition.DatabaseId, state.Lane.Queue, Capability.QueueInspect)], [], false, token);
        var failures = new List<Exception>();
        RequestCqrsRf3Callers? denied = null;
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            denied = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node2, identity.Secret, token);
            var request = new InspectMessageRequest(state.Lane, QueueDeadLetterRf3Protocol.Message);
            await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(await denied.Sdk.InspectAsync(request, token), ErrorCode.PermissionDenied);
            var reply = await denied.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, identity.Secret, QueueDeadLetterRf3Protocol.Payload);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, identity.Secret, QueueDeadLetterRf3Protocol.Headers);
        }, failures);
        if (denied is not null)
        { await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => denied.DisposeAsync().AsTask(), failures); }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task LeasedAsync(RequestCqrsRf3Callers callers, QueueLaneRef lane,
        Delivery delivery, long sequence, CancellationToken token)
    {
        var expected = new MessageInspection(new(delivery.Id, MessageState.Leased, QueueDeadLetterRf3Protocol.One,
            QueueDeadLetterRf3Protocol.ClaimVersion, sequence, null, null, QueueDeadLetterRf3Protocol.Root,
            QueueDeadLetterRf3Protocol.First, delivery.LeaseUntil), QueueDeadLetterRf3Protocol.Payload, QueueDeadLetterRf3Protocol.Headers);
        var request = new InspectMessageRequest(lane, delivery.Id);
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.InspectAsync(request, token)), expected);
        await EqualAsync((await McpCallerAssertions.SuccessAsync<MessageInspection>(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesInspect, request, token))).Value, expected);
    }

    internal static async Task MutationAsync(CommitReceipt actual, Guid command, QueueLaneRef lane,
        string id, string kind, long version)
    {
        await Assert.That(actual.CommandId).IsEqualTo(command);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(lane.Partition.AtomicPartitionId);
        MutationReceipt[] expected = [new(kind, lane.Queue, id, version)];
        await Assert.That(JsonDefaults.Serialize(actual.Mutations).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        foreach (var mutation in actual.Mutations)
        {
            using var json = global::System.Text.Json.JsonDocument.Parse(JsonDefaults.Serialize(mutation));
            await Assert.That(json.RootElement.TryGetProperty(PrivateCompositionReferencesProperty, out _)).IsFalse();
        }
    }

    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
