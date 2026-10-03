using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-004/005: an actual atomic event and message share SQL results, unchanged inspection and fenced acknowledgement.</summary>
internal static class IsolatedKeyLoadPublicRegressionMessaging
{
    private const string InvalidToken = "isolated-invalid-delivery-token";
    private const string AppendKind = "appendEvents";
    private const string EnqueueKind = "enqueue";

    internal static async Task VerifyAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicRegressionMcp mcp,
        IsolatedKeyLoadPublicRegressionScenario scenario, CancellationToken token)
    {
        var command = scenario.Command(new AppendEvents(scenario.Stream.StreamSet, scenario.Stream.StreamId,
            [new(scenario.EventId, "isolated-event", scenario.InitialJson)], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(scenario.Lane.Queue, scenario.MessageId, scenario.InitialJson));
        var receipt = await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.CommitAsync(command, token));
        await IsolatedKeyLoadPublicRegressionAssertions.ReceiptAsync(receipt, command.CommandId, 2, scenario.Partition);
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt(AppendKind, scenario.Stream.StreamSet, scenario.Stream.StreamId, 1));
        await Assert.That(receipt.Mutations[1].Kind).IsEqualTo(EnqueueKind);
        await Assert.That(receipt.Mutations[1].Resource).IsEqualTo(scenario.Lane.Queue);
        await Assert.That(receipt.Mutations[1].Id).IsEqualTo(scenario.MessageId);
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(receipt,
            await mcp.SuccessAsync<CommitReceipt>(IsolatedKeyLoadPublicRegressionProtocol.Commit, command, token));
        await VerifyStreamAsync(sdk, mcp, scenario, receipt.Token.Position, token);
        await VerifyInspectionAsync(sdk, mcp, scenario, token);
        await VerifyLeaseAsync(sdk, mcp, scenario, token);
    }

    private static async Task VerifyStreamAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicRegressionMcp mcp,
        IsolatedKeyLoadPublicRegressionScenario scenario, long position, CancellationToken token)
    {
        var request = new ReadStreamRequest(scenario.Stream);
        var native = await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.ReadStreamAsync(request, token));
        var sql = IsolatedKeyLoadPublicRegressionProtocol.Call(scenario.Partition, IsolatedKeyLoadPublicRegressionProtocol.StreamRead, request);
        var official = await mcp.SuccessAsync<StreamPage>(SqlOperationProtocol.ToolName, sql, token);
        await Assert.That(native.Events).HasSingleItem();
        await Assert.That(native.Events[0].Data.EventId).IsEqualTo(scenario.EventId);
        await Assert.That(native.Events[0].Data.PayloadJson).IsEqualTo(scenario.InitialJson);
        await Assert.That(native.Events[0].Revision).IsEqualTo(1L);
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(native.Events, official.Events);
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(native.Head, official.Head);
        await Assert.That(official.Stream).IsEqualTo(native.Stream);
        await Assert.That(official.HasMore).IsEqualTo(native.HasMore);
        await Assert.That(native.CutPosition).IsGreaterThanOrEqualTo(position);
        await Assert.That(official.CutPosition).IsGreaterThanOrEqualTo(position);
    }

    private static async Task VerifyInspectionAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicRegressionMcp mcp,
        IsolatedKeyLoadPublicRegressionScenario scenario, CancellationToken token)
    {
        var request = scenario.Inspect;
        var before = await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.InspectAsync(request, token));
        await Assert.That(before!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(before.Metadata.Attempts).IsEqualTo(0);
        await Assert.That(before.Metadata.LeaseOwner).IsNull();
        await Assert.That(before.PayloadJson).IsEqualTo(scenario.InitialJson);
        var sql = IsolatedKeyLoadPublicRegressionProtocol.Call(scenario.Partition, IsolatedKeyLoadPublicRegressionProtocol.Inspect, request);
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(before,
            await mcp.SuccessAsync<MessageInspection>(SqlOperationProtocol.ToolName, sql, token));
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(before,
            await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.InspectAsync(request, token)));
    }

    private static async Task VerifyLeaseAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicRegressionMcp mcp,
        IsolatedKeyLoadPublicRegressionScenario scenario, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), scenario.Lane);
        var received = await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.ReceiveAsync(request, token));
        await Assert.That(received.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(received.Deliveries).HasSingleItem();
        var delivery = received.Deliveries[0];
        await Assert.That(delivery.Id).IsEqualTo(scenario.MessageId);
        await Assert.That(delivery.PayloadJson).IsEqualTo(scenario.InitialJson);
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(received,
            await mcp.SuccessAsync<ReceiveResult>(IsolatedKeyLoadPublicRegressionProtocol.Receive, request, token));
        var leased = await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.InspectAsync(scenario.Inspect, token));
        var forged = new DeliveryCommand(Guid.NewGuid(), scenario.Lane, InvalidToken, DeliveryAction.Ack);
        await mcp.ErrorAsync(SqlOperationProtocol.ToolName,
            IsolatedKeyLoadPublicRegressionProtocol.Call(scenario.Partition, IsolatedKeyLoadPublicRegressionProtocol.Complete, forged),
            ErrorCode.TokenInvalidated, token);
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(leased,
            await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.InspectAsync(scenario.Inspect, token)));
        await VerifyAckAsync(sdk, mcp, scenario, delivery, leased!, token);
    }

    private static async Task VerifyAckAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicRegressionMcp mcp,
        IsolatedKeyLoadPublicRegressionScenario scenario, Delivery delivery, MessageInspection leased, CancellationToken token)
    {
        var command = new DeliveryCommand(Guid.NewGuid(), scenario.Lane, delivery.Token, DeliveryAction.Ack);
        var receipt = await mcp.SuccessAsync<CommitReceipt>(IsolatedKeyLoadPublicRegressionProtocol.Complete, command, token);
        await IsolatedKeyLoadPublicRegressionAssertions.ReceiptAsync(receipt, command.CommandId, 1, scenario.Partition);
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt(nameof(DeliveryAction.Ack),
            scenario.Lane.Queue, scenario.MessageId, leased.Metadata.StateVersion + 1));
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(receipt,
            await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.CompleteAsync(command, token)));
        var acknowledged = await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.InspectAsync(scenario.Inspect, token));
        await Assert.That(acknowledged!.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(acknowledged.Metadata.LeaseOwner).IsNull();
        await Assert.That(acknowledged.PayloadJson).IsNull();
        await Assert.That(acknowledged.HeadersJson).IsNull();
        await IsolatedKeyLoadPublicRegressionAssertions.ErrorAsync(
            await sdk.CompleteAsync(command with { CommandId = Guid.NewGuid() }, token), ErrorCode.StaleLease);
        await Assert.That((await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(
            await sdk.ReceiveAsync(new(Guid.NewGuid(), scenario.Lane), token))).Deliveries).IsEmpty();
    }
}
