using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.ClusterRouting;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class MultiLaneConnectionOwnerTrial
{
    private const int LaneCount = 2;
    private const long HealthyRevision = 1;
    private const string HealthyDocument = "multi-lane-call-local-healthy";
    private const string MessageId = "message";

    internal static Task RunAsync() => ConnectionNativeScenario.RunAsync(async scenario =>
    {
        using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
        var token = timeout.Token;
        await scenario.InitializeAsync(token);
        var lanes = MultiLaneReceiveNativeFlow.Seed(scenario.Fixture, LaneCount);
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(),
            [new(Guid.NewGuid(), lanes[0]), new(Guid.NewGuid(), lanes[1])]);
        var reply = await InvokeAsync(scenario, OperationKind.ReceiveAcrossLanes, request.RequestId, request, token);
        await Assert.That(reply.Error).IsNull();
        var result = MultiLaneReceiveNativeFlow.Value(reply);
        await Assert.That(result.Outcomes.Length).IsEqualTo(LaneCount);
        await RequireOwnersAsync(scenario, request, token);
        for (var index = 0; index < LaneCount; index++)
        { await MultiLaneReceiveNativeFlow.CommittedAsync(result.Outcomes[index], request.Requests[index]); }
        await ReplayAsync(scenario, request, result, token);
        await AckAsync(scenario, request, result, token);
        await HealthyAsync(scenario, token);
        await scenario.CloseAsync(token);
    });

    private static async Task<GrainOperationReply> InvokeAsync<T>(ConnectionNativeScenario scenario,
        OperationKind kind, Guid commandId, T request, CancellationToken token)
    {
        var fixture = scenario.Fixture;
        var principal = GrainRequestAuthority.Reload(fixture.Database.Database, ConnectionNativeProtocol.Root,
            fixture.Clock);
        var actor = Guid.NewGuid();
        var signed = fixture.Codec.CreateCommand(actor, principal.Id, kind, commandId,
            NativeSerialization.Serialize(request));
        return await scenario.InvokeAsync(principal, actor, commandId, signed, token);
    }

    private static async Task RequireOwnersAsync(ConnectionNativeScenario scenario,
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
            await Assert.That(child.Identity.PrincipalId).IsEqualTo(ConnectionNativeProtocol.Root);
            await child.Disposed.Task.WaitAsync(token);
        }
        await scenario.AssertActivationCountAsync(ConnectionNativeProtocol.OneConnection, token);
    }

    private static async Task ReplayAsync(ConnectionNativeScenario scenario, MultiLaneReceiveRequest request,
        MultiLaneReceiveResult original, CancellationToken token)
    {
        var store = scenario.Fixture.Database.Store;
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var retry = request with { RequestId = Guid.NewGuid() };
        var reply = await InvokeAsync(scenario, OperationKind.ReceiveAcrossLanes, retry.RequestId, retry, token);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(NativeSerialization.Serialize(MultiLaneReceiveNativeFlow.Value(reply).Outcomes)
            .SequenceEqual(NativeSerialization.Serialize(original.Outcomes))).IsTrue();
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
    }

    private static async Task AckAsync(ConnectionNativeScenario scenario, MultiLaneReceiveRequest request,
        MultiLaneReceiveResult original, CancellationToken token)
    {
        for (var index = 0; index < LaneCount; index++)
        {
            var delivery = original.Outcomes[index].Result!.Deliveries.Single();
            var id = Guid.NewGuid();
            var command = new DeliveryCommand(id, request.Requests[index].Lane, delivery.Token, DeliveryAction.Ack);
            var reply = await InvokeAsync(scenario, OperationKind.Delivery, id, command, token);
            await Assert.That(reply.Error).IsNull();
            var inspected = scenario.Fixture.Database.Database.InspectMessage(ConnectionNativeProtocol.Root,
                request.Requests[index].Lane, MessageId)!;
            await Assert.That(inspected.Metadata.State).IsEqualTo(MessageState.Acked);
        }
    }

    private static async Task HealthyAsync(ConnectionNativeScenario scenario, CancellationToken token)
    {
        var principal = GrainRequestAuthority.Reload(scenario.Fixture.Database.Database, ConnectionNativeProtocol.Root,
            scenario.Fixture.Clock);
        var command = scenario.Command(HealthyDocument);
        var reply = await scenario.CommandAsync(principal, Guid.NewGuid(), command, token);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(ConnectionNativeAssertions.Value<CommitReceipt>(reply).CommandId).IsEqualTo(command.CommandId);
        await ConnectionNativeAssertions.StoredAsync(scenario, HealthyDocument, HealthyRevision, ConnectionNativeProtocol.FirstJson);
    }
}
