using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueDeadLetterClusterAssertions
{
    private const string PrivateCompositionReferencesProperty = "compositionReferences";
    internal static async Task DeliveryAsync(Delivery actual, string id)
    {
        await Assert.That(actual.Id).IsEqualTo(id);
        await Assert.That(actual.PayloadJson).IsEqualTo(QueueDeadLetterClusterProtocol.Payload);
        await Assert.That(actual.HeadersJson).IsEqualTo(QueueDeadLetterClusterProtocol.Headers);
        await Assert.That(actual.Attempt).IsEqualTo(QueueDeadLetterClusterProtocol.One);
        await Assert.That(actual.LeaseVersion).IsEqualTo(QueueDeadLetterClusterProtocol.First);
        await Assert.That(actual.DeliveryGeneration).IsEqualTo(QueueDeadLetterClusterProtocol.First);
        await Assert.That(actual.Token).IsNotEmpty();
    }

    internal static async Task LiteralAsync(RequestCqrsClusterCallers callers, QueueDeadLetterClusterState state, CancellationToken token)
    {
        var request = new InspectMessageRequest(state.Lane, QueueDeadLetterClusterProtocol.Message);
        var expected = new MessageInspection(new(QueueDeadLetterClusterProtocol.Message, MessageState.DeadLettered,
            QueueDeadLetterClusterProtocol.One, QueueDeadLetterClusterProtocol.TerminalVersion, QueueDeadLetterClusterProtocol.First,
            null, null, LeaseVersion: QueueDeadLetterClusterProtocol.First, SafeFailureCode: QueueDeadLetterClusterProtocol.Exhausted, ParkedSequence: QueueDeadLetterClusterProtocol.First),
            QueueDeadLetterClusterProtocol.Payload, QueueDeadLetterClusterProtocol.Headers);
        await EqualAsync(await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(request, token)), expected);
        await EqualAsync((await PublicReadAssertions.MessageAsync(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesInspect, request, token))).Value, expected);
    }

    internal static async Task ReceiptAsync(RequestCqrsClusterCallers callers, CommandRequest request,
        CommitReceipt original, CancellationToken token)
    {
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(request, token)), original);
        await EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, request, token))).Value, original);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers callers, QueueDeadLetterClusterState state, CancellationToken token)
    {
        await ReceiptAsync(callers, state.Producer, state.Produced, token);
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteAsync(state.Nack, token)), state.Completed);
        await EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesComplete, state.Nack, token))).Value, state.Completed);
        await RefusalAsync(callers, state, token);
        await LiteralAsync(callers, state, token);
    }

    internal static async Task RefusalAsync(RequestCqrsClusterCallers callers, QueueDeadLetterClusterState state, CancellationToken token)
    {
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(await callers.Sdk.CommitAsync(state.Refused, token), ErrorCode.ResourceExhausted);
        _ = (await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
            state.Refused, token), ErrorCode.ResourceExhausted, dispatched: true)).Value;
        await Assert.That(await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(
            new(state.Partition, QueueDeadLetterClusterProtocol.Collection, QueueDeadLetterClusterProtocol.Refused), token))).IsNull();
        await Assert.That(await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(
            new(state.Lane, QueueDeadLetterClusterProtocol.Refused), token))).IsNull();
    }

    internal static async Task DeniedAsync(ClusterFixture fixture, QueueDeadLetterClusterState state, CancellationToken token)
    {
        var identity = await MessagingClusterIdentity.CreateAsync(fixture, state.Partition.TenantId,
            [new(state.Partition.DatabaseId, state.Lane.Queue, Capability.QueueInspect)], [], false, token);
        var failures = new List<Exception>();
        RequestCqrsClusterCallers? denied = null;
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            denied = await RequestCqrsClusterCallers.ConnectAsync(fixture.App, McpCallerProtocol.Node2, identity.Secret, token);
            var request = new InspectMessageRequest(state.Lane, QueueDeadLetterClusterProtocol.Message);
            await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(await denied.Sdk.InspectAsync(request, token), ErrorCode.PermissionDenied);
            var reply = await denied.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token);
            _ = (await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true)).Value;
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, identity.Secret, QueueDeadLetterClusterProtocol.Payload);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, identity.Secret, QueueDeadLetterClusterProtocol.Headers);
        }, failures);
        if (denied is not null)
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => denied.DisposeAsync().AsTask(), failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task LeasedAsync(RequestCqrsClusterCallers callers, QueueLaneRef lane,
        Delivery delivery, long sequence, CancellationToken token)
    {
        var expected = new MessageInspection(new(delivery.Id, MessageState.Leased, QueueDeadLetterClusterProtocol.One,
            QueueDeadLetterClusterProtocol.ClaimVersion, sequence, null, null, QueueDeadLetterClusterProtocol.Root,
            QueueDeadLetterClusterProtocol.First, delivery.LeaseUntil), QueueDeadLetterClusterProtocol.Payload, QueueDeadLetterClusterProtocol.Headers);
        var request = new InspectMessageRequest(lane, delivery.Id);
        await EqualAsync(await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(request, token)), expected);
        await EqualAsync((await PublicReadAssertions.MessageAsync(await callers.Mcp.CallAsync(
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
