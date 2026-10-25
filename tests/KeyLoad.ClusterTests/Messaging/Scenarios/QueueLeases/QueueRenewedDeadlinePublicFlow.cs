using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueRenewedDeadlinePublicFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var seed = await QueueLeaseClusterSetup.CreateAsync(fixture, token);
        var state = await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node2, seed.Worker.Secret,
            callers => WarmAsync(callers, seed, token), token);
        await QueueLeaseClusterCold.RestartAsync(fixture, token);
        await ProducerReplayAsync(fixture, state, token);
        await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node3, seed.Worker.Secret,
            async callers => { await FirstColdAsync(callers, state, token); return true; }, token);
        await QueueLeaseClusterCold.RestartAsync(fixture, token);
        await ProducerReplayAsync(fixture, state, token);
        var settled = await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node2, seed.Worker.Secret,
            callers => SecondColdAsync(callers, state, token), token);
        await HealthyAsync(fixture, settled, token);
    }

    private static async Task<QueueRenewedDeadlinePublicState> WarmAsync(RequestCqrsClusterCallers callers,
        QueueLeaseClusterSeed seed, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), seed.Lane);
        await Assert.That(request.LeaseSeconds).IsEqualTo(30);
        await Assert.That(seed.Resource.QueuePolicy.MaxLeaseSeconds).IsEqualTo(300);
        var claim = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(request, token));
        var delivery = await Assert.That(claim.Deliveries).HasSingleItem();
        await QueueLeaseClusterAssertions.LiteralDeliveryAsync(delivery, QueueLeaseClusterProtocol.Message, QueueLeaseClusterProtocol.Payload);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        var leased = new MessageInspection(new(QueueLeaseClusterProtocol.Message, MessageState.Leased, 1, 2L, 1L,
            null, null, seed.Worker.Principal.Id, 1L, delivery.LeaseUntil),
            QueueLeaseClusterProtocol.Payload, QueueLeaseClusterProtocol.Headers);
        await QueueRenewedDeadlinePublicAssertions.ImagesAsync(callers, seed, leased, token);
        var renew = new DeliveryCommand(Guid.NewGuid(), seed.Lane, delivery.Token, DeliveryAction.Renew,
            seed.Resource.QueuePolicy.MaxLeaseSeconds);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteAsync(renew, token));
        await QueueRenewedDeadlinePublicAssertions.ReceiptAsync(receipt, renew.CommandId, seed.Lane,
            QueueLeaseClusterProtocol.Message, "Renew", 3L);
        var observed = await QueueLeaseClusterAssertions.InspectAsync(callers.Sdk, seed.Lane, token);
        await Assert.That(observed.Metadata.LeaseUntil).IsNotNull();
        var until = observed.Metadata.LeaseUntil
            ?? throw new InvalidOperationException("The successful renewal omitted its current deadline.");
        await Assert.That(until).IsGreaterThan(delivery.LeaseUntil);
        var renewed = leased with
        {
            Metadata = leased.Metadata with
            {
                StateVersion = 3L,
                LeaseUntil = until
            }
        };
        var state = new QueueRenewedDeadlinePublicState(seed, request, claim, delivery, renew, receipt, renewed, []);
        await QueueRenewedDeadlinePublicAssertions.LiveOriginalAsync(callers, state, token);
        await Assert.That(TimeProvider.System.GetUtcNow()).IsLessThan(delivery.LeaseUntil);
        await AddRefusalAsync(callers, state, Deadline(state, 2L, delivery.LeaseUntil),
            ErrorCode.RevisionConflict, token);
        await AddRefusalAsync(callers, state, Deadline(state, 3L, until), ErrorCode.Conflict, token);
        return state;
    }

    private static CommandRequest Deadline(QueueRenewedDeadlinePublicState state, long version, DateTimeOffset due)
        => new(Guid.NewGuid(), state.Seed.Lane.Partition,
            [new AdvanceQueueDeadline(state.Seed.Lane.Queue, QueueLeaseClusterProtocol.Message,
                MessageState.Leased, version, 1L, due, QueueDeadlineKind.ExpireLease)]);

    private static async Task AddRefusalAsync(RequestCqrsClusterCallers callers,
        QueueRenewedDeadlinePublicState state, CommandRequest request, ErrorCode code, CancellationToken token)
    {
        var detail = QueueRenewedDeadlinePublicAssertions.Detail(code);
        var original = await QueueRenewedDeadlinePublicAssertions.RefusalAsync(callers, request, code, detail, token);
        state.Refusals.Add(new(request, code, detail, original));
        await QueueRenewedDeadlinePublicAssertions.ImagesAsync(callers, state.Seed, state.Renewed, token);
    }

    private static async Task FirstColdAsync(RequestCqrsClusterCallers callers,
        QueueRenewedDeadlinePublicState state, CancellationToken token)
    {
        await QueueRenewedDeadlinePublicAssertions.LiveOriginalAsync(callers, state, token);
        await QueueRenewedDeadlinePublicAssertions.RefusalsAsync(callers, state, state.Renewed, token);
        await WaitForOriginalDeadlineAsync(state.Delivery.LeaseUntil, token);
        await Assert.That(TimeProvider.System.GetUtcNow()).IsGreaterThanOrEqualTo(state.Delivery.LeaseUntil);
        await QueueRenewedDeadlinePublicAssertions.LiveOriginalAsync(callers, state, token);
        await AddRefusalAsync(callers, state, Deadline(state, 2L, state.Delivery.LeaseUntil),
            ErrorCode.RevisionConflict, token);
        await QueueRenewedDeadlinePublicAssertions.RefusalsAsync(callers, state, state.Renewed, token);
    }

    private static async Task WaitForOriginalDeadlineAsync(DateTimeOffset original, CancellationToken token)
    {
        while (true)
        {
            var remaining = original - TimeProvider.System.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }
            await Task.Delay(remaining, TimeProvider.System, token);
        }
    }

    private static async Task<QueueRenewedDeadlineSettled> SecondColdAsync(RequestCqrsClusterCallers callers,
        QueueRenewedDeadlinePublicState state, CancellationToken token)
    {
        await QueueRenewedDeadlinePublicAssertions.LiveOriginalAsync(callers, state, token);
        await QueueRenewedDeadlinePublicAssertions.RefusalsAsync(callers, state, state.Renewed, token);
        var ack = new DeliveryCommand(Guid.NewGuid(), state.Seed.Lane, state.Delivery.Token, DeliveryAction.Ack);
        var receipt = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesComplete, ack, token));
        await QueueRenewedDeadlinePublicAssertions.ReceiptAsync(receipt.Value, ack.CommandId, state.Seed.Lane,
            QueueLeaseClusterProtocol.Message, "Ack", 4L);
        var acked = new MessageInspection(state.Renewed.Metadata with
        {
            State = MessageState.Acked,
            StateVersion = 4L,
            LeaseOwner = null,
            LeaseUntil = null
        }, null, null);
        var settled = new QueueRenewedDeadlineSettled(state, ack, receipt.Value, acked);
        await QueueRenewedDeadlinePublicAssertions.SettledOriginalAsync(callers, settled, token);
        return settled;
    }

    private static Task<bool> ProducerReplayAsync(ClusterFixture fixture,
        QueueRenewedDeadlinePublicState state, CancellationToken token)
        => QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
        {
            await QueueLeaseClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await callers.Sdk.CommitAsync(state.Seed.Enqueue, token)), state.Seed.EnqueueReceipt);
            var reply = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed.Enqueue, token));
            await QueueLeaseClusterAssertions.EqualAsync(reply.Value, state.Seed.EnqueueReceipt);
            return true;
        }, token);

    private static async Task HealthyAsync(ClusterFixture fixture,
        QueueRenewedDeadlineSettled settled, CancellationToken token)
    {
        var state = settled.Original;
        var command = new CommandRequest(Guid.NewGuid(), state.Seed.Lane.Partition,
            [new EnqueueMessage(state.Seed.Lane.Queue, QueueLeaseClusterProtocol.Healthy,
                QueueLeaseClusterProtocol.HealthyPayload, QueueLeaseClusterProtocol.Headers)]);
        var receipt = await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey,
            async callers =>
            {
                await QueueLeaseClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                    await callers.Sdk.CommitAsync(state.Seed.Enqueue, token)), state.Seed.EnqueueReceipt);
                var actual = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token));
                var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token));
                await QueueLeaseClusterAssertions.EqualAsync(replay.Value, actual);
                return actual;
            }, token);
        await QueueRenewedDeadlinePublicAssertions.ReceiptAsync(receipt, command.CommandId, state.Seed.Lane,
            QueueLeaseClusterProtocol.Healthy, "enqueue", 1L);
        await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node3, state.Seed.Worker.Secret,
            async callers =>
            {
                await HealthyClaimAsync(callers, settled, token);
                return true;
            }, token);
    }

    private static async Task HealthyClaimAsync(RequestCqrsClusterCallers callers,
        QueueRenewedDeadlineSettled settled, CancellationToken token)
    {
        var state = settled.Original;
        var request = new ReceiveRequest(Guid.NewGuid(), state.Seed.Lane);
        var claim = await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token));
        var delivery = await Assert.That(claim.Value.Deliveries).HasSingleItem();
        await QueueLeaseClusterAssertions.LiteralDeliveryAsync(delivery, QueueLeaseClusterProtocol.Healthy, QueueLeaseClusterProtocol.HealthyPayload);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        await QueueLeaseClusterAssertions.EqualAsync(
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(request, token)), claim.Value);
        var ack = new DeliveryCommand(Guid.NewGuid(), state.Seed.Lane, delivery.Token, DeliveryAction.Ack);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteAsync(ack, token));
        await QueueRenewedDeadlinePublicAssertions.ReceiptAsync(receipt, ack.CommandId, state.Seed.Lane,
            QueueLeaseClusterProtocol.Healthy, "Ack", 3L);
        await QueueLeaseClusterAssertions.ReplayedAsync(callers.Sdk, callers.Mcp, ack, receipt, token);
        await QueueLeaseClusterAssertions.DeniedAsync(await callers.Sdk.ReceiveAsync(request, token), ErrorCode.StaleLease);
        _ = (await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, request, token), ErrorCode.StaleLease, true)).Value;
        var expected = new MessageInspection(new(QueueLeaseClusterProtocol.Healthy, MessageState.Acked,
            1, 3L, 2L, null, null, LeaseVersion: 1L), null, null);
        await QueueLeaseClusterAssertions.EqualAsync(await PublicReadAssertions.MessageAsync(
            await callers.Sdk.InspectAsync(new(state.Seed.Lane, QueueLeaseClusterProtocol.Healthy), token)), expected);
        var inspected = await PublicReadAssertions.RequiredMessageAsync(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesInspect, new InspectMessageRequest(state.Seed.Lane, QueueLeaseClusterProtocol.Healthy), token));
        await QueueLeaseClusterAssertions.EqualAsync(inspected.Value, expected);
        await QueueRenewedDeadlinePublicAssertions.SettledOriginalAsync(callers, settled, token);
        var empty = await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ReceiveAsync(new(Guid.NewGuid(), state.Seed.Lane), token));
        await Assert.That(empty.Deliveries).IsEmpty();
    }
}
