using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.OrleansExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class MultiLaneConnectionOwnerTrial
{
    private const int LaneCount = 2;
    private const long HealthyRevision = 1;
    private const string HealthyDocument = "multi-lane-call-local-healthy";
    private const string MessageId = "message";

    internal static Task RunAsync() => ConnectionScenario.RunAsync(async scenario =>
    {
        using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
        var token = timeout.Token;
        await scenario.InitializeAsync(token);
        var lanes = MultiLaneReceiveFlow.Seed(scenario.Fixture, LaneCount);
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(),
            [new(Guid.NewGuid(), lanes[0]), new(Guid.NewGuid(), lanes[1])]);
        var reply = await InvokeAsync(scenario, OperationKind.ReceiveAcrossLanes, request.RequestId, request, token);
        await Assert.That(reply.Error).IsNull();
        var result = MultiLaneReceiveFlow.Value(reply);
        await Assert.That(result.Outcomes.Length).IsEqualTo(LaneCount);
        await RequireOwnersAsync(scenario, request, token);
        for (var index = 0; index < LaneCount; index++)
        {
            await MultiLaneReceiveFlow.CommittedAsync(result.Outcomes[index], request.Requests[index]);
        }
        await ReplayAsync(scenario, request, result, token);
        await AckAsync(scenario, request, result, token);
        await HealthyAsync(scenario, token);
        await scenario.CloseAsync(token);
    });

    private static async Task<GrainOperationReply> InvokeAsync<T>(ConnectionScenario scenario,
        OperationKind kind, Guid commandId, T request, CancellationToken token)
    {
        var fixture = scenario.Fixture;
        var principal = GrainRequestAuthority.Reload(fixture.Database.Database, ConnectionProtocol.Root,
            fixture.Clock);
        var actor = Guid.NewGuid();
        var envelope = fixture.Codec.CreateCommand(actor, principal.Id, kind, commandId,
            OrleansSerialization.Serialize(request));
        return await scenario.InvokeAsync(principal, actor, commandId, envelope, token);
    }

    private static async Task RequireOwnersAsync(ConnectionScenario scenario,
        MultiLaneReceiveRequest request, CancellationToken token)
    {
        var parent = scenario.Observation.ForCommand(request.RequestId);
        await Assert.That(parent.Identity.CommandKind).IsEqualTo(OperationKind.ReceiveAcrossLanes);
        await parent.Disposed.Task.WaitAsync(token);
        foreach (var leaf in request.Requests)
        {
            var child = scenario.Observation.ForCommand(leaf.RequestId);
            await Assert.That(child.GrainId).IsEqualTo(parent.GrainId);
            await Assert.That(child.ActivationId).IsEqualTo(parent.ActivationId);
            await Assert.That(child.Identity.RequestId).IsNotEqualTo(parent.Identity.RequestId);
            await Assert.That(child.Identity.CommandKind).IsEqualTo(OperationKind.Receive);
            await Assert.That(child.Identity.PrincipalId).IsEqualTo(ConnectionProtocol.Root);
            await child.Disposed.Task.WaitAsync(token);
        }
        await scenario.AssertActivationCountAsync(ConnectionProtocol.OneConnection, token);
    }

    private static async Task ReplayAsync(ConnectionScenario scenario, MultiLaneReceiveRequest request,
        MultiLaneReceiveResult original, CancellationToken token)
    {
        var store = scenario.Fixture.Database.Store;
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var retry = request with
        {
            RequestId = Guid.NewGuid()
        };
        var reply = await InvokeAsync(scenario, OperationKind.ReceiveAcrossLanes, retry.RequestId, retry, token);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(OrleansSerialization.Serialize(MultiLaneReceiveFlow.Value(reply).Outcomes)
            .SequenceEqual(OrleansSerialization.Serialize(original.Outcomes))).IsTrue();
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
    }

    private static async Task AckAsync(ConnectionScenario scenario, MultiLaneReceiveRequest request,
        MultiLaneReceiveResult original, CancellationToken token)
    {
        for (var index = 0; index < LaneCount; index++)
        {
            var delivery = original.Outcomes[index].Result!.Deliveries.Single();
            var id = Guid.NewGuid();
            var command = new DeliveryCommand(id, request.Requests[index].Lane, delivery.Token, DeliveryAction.Ack);
            var reply = await InvokeAsync(scenario, OperationKind.Delivery, id, command, token);
            await Assert.That(reply.Error).IsNull();
            var inspected = scenario.Fixture.Database.Database.InspectMessage(ConnectionProtocol.Root,
                request.Requests[index].Lane, MessageId)!;
            await Assert.That(inspected.Metadata.State).IsEqualTo(MessageState.Acked);
        }
    }

    private static async Task HealthyAsync(ConnectionScenario scenario, CancellationToken token)
    {
        var principal = GrainRequestAuthority.Reload(scenario.Fixture.Database.Database, ConnectionProtocol.Root,
            scenario.Fixture.Clock);
        var command = scenario.Command(HealthyDocument);
        var reply = await scenario.CommandAsync(principal, Guid.NewGuid(), command, token);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(ConnectionAssertions.Value<CommitReceipt>(reply).CommandId).IsEqualTo(command.CommandId);
        await ConnectionAssertions.StoredAsync(scenario, HealthyDocument, HealthyRevision, ConnectionProtocol.FirstJson);
    }
}
