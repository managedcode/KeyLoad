using System.Collections.Immutable;
using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.ClusterRouting;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

/// <summary>AC-MSG-007: real native request actors and canonical ZoneTree independent claims.</summary>
[RequestCqrsDataSource, NotInParallel]
internal sealed class MultiLaneReceiveWholeFlowTests(RequestCqrsClusterFixture fixture)
{
    [Test]
    public async Task TwoCommittedLanesReplayExactlyWithoutNewEffectsAndAckHealthy()
    {
        var lanes = MultiLaneReceiveNativeFlow.Seed(fixture, 2);
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(),
            [new(Guid.NewGuid(), lanes[0]), new(Guid.NewGuid(), lanes[1])]);
        var reply = await MultiLaneReceiveNativeFlow.ReceiveAsync(fixture, request);
        await Assert.That(reply.Error).IsNull().Because(MultiLaneReceiveFailureObservation.Summary(fixture));
        var result = MultiLaneReceiveNativeFlow.Value(reply);
        await Assert.That(result.Outcomes.Length).IsEqualTo(2);
        for (var index = 0; index < 2; index++)
        {
            await MultiLaneReceiveNativeFlow.CommittedAsync(result.Outcomes[index], request.Requests[index]);
            var delivery = result.Outcomes[index].Result!.Deliveries[0];
            var inspected = fixture.Database.Database.InspectMessage("root", lanes[index], "message")!;
            await Assert.That(inspected.Metadata.LeaseUntil).IsEqualTo(delivery.LeaseUntil);
            await Assert.That(inspected.Metadata.LeaseOwner).IsEqualTo("root");
            var claims = fixture.Database.Database.Verify<DeliveryClaims>(delivery.Token);
            await Assert.That(claims.Lane).IsEqualTo(lanes[index]);
            await Assert.That(claims.MessageId).IsEqualTo(delivery.Id);
            await Assert.That(claims.PrincipalId).IsEqualTo("root");
            await Assert.That(claims.LeaseVersion).IsEqualTo(delivery.LeaseVersion);
            await Assert.That(claims.DeliveryGeneration).IsEqualTo(delivery.DeliveryGeneration);
            await Assert.That(claims.Incarnation).IsEqualTo(fixture.Database.Store.Identity.Incarnation);
        }
        var state = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var position = fixture.Database.Store.Position;
        var retry = await MultiLaneReceiveNativeFlow.ReceiveAsync(fixture, request with { RequestId = Guid.NewGuid() });
        await Assert.That(retry.Error).IsNull().Because(MultiLaneReceiveFailureObservation.Summary(fixture));
        await Assert.That(NativeSerialization.Serialize(MultiLaneReceiveNativeFlow.Value(retry).Outcomes)
            .SequenceEqual(NativeSerialization.Serialize(result.Outcomes))).IsTrue();
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store)).IsEquivalentTo(state, CollectionOrdering.Matching);
        for (var index = 0; index < 2; index++)
        {
            var delivery = result.Outcomes[index].Result!.Deliveries[0];
            var id = Guid.NewGuid();
            var ack = await MultiLaneReceiveNativeFlow.InvokeAsync(fixture, OperationKind.Delivery, id,
                new DeliveryCommand(id, lanes[index], delivery.Token, DeliveryAction.Ack), "root", default);
            await Assert.That(ack.Error).IsNull().Because(MultiLaneReceiveFailureObservation.Summary(fixture));
            await Assert.That(fixture.Database.Database.InspectMessage("root", lanes[index], "message")!.Metadata.State)
                .IsEqualTo(MessageState.Acked);
        }
        await HealthyAsync();
    }

    [Test]
    public async Task DeniedMiddleLanePreservesItsMessageAndFollowingLaneCommitsWithExactRetry()
    {
        var lanes = MultiLaneReceiveNativeFlow.Seed(fixture, 3);
        var principal = new PrincipalRecord("multi-worker-" + Guid.NewGuid().ToString("N"),
            fixture.Database.Partition.TenantId,
            [new ScopeGrant(fixture.Database.Partition.DatabaseId, lanes[0].Queue, Capability.QueueConsume),
             new ScopeGrant(fixture.Database.Partition.DatabaseId, lanes[2].Queue, Capability.QueueConsume)], []);
        fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal));
        var beforeDenied = NativeSerialization.Serialize(fixture.Database.Database.InspectMessage("root", lanes[1], "message")!);
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(), [.. lanes.Select(lane => new ReceiveRequest(Guid.NewGuid(), lane))]);
        var reply = await MultiLaneReceiveNativeFlow.ReceiveAsync(fixture, request, principal.Id);
        await Assert.That(reply.Error).IsNull().Because(MultiLaneReceiveFailureObservation.Summary(fixture));
        var result = MultiLaneReceiveNativeFlow.Value(reply);
        await Assert.That(result.Outcomes.Length).IsEqualTo(3);
        await MultiLaneReceiveNativeFlow.CommittedAsync(result.Outcomes[0], request.Requests[0]);
        await MultiLaneReceiveNativeFlow.CommittedAsync(result.Outcomes[2], request.Requests[2]);
        var denied = result.Outcomes[1];
        await Assert.That(denied.Status).IsEqualTo(QueueLaneReceiveStatus.Rejected);
        await Assert.That(denied.RequestId).IsEqualTo(request.Requests[1].RequestId);
        await Assert.That(denied.Lane).IsEqualTo(lanes[1]);
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(denied.Result).IsNull();
        await Assert.That(denied.SafeDetail).IsEqualTo("The principal cannot perform this operation in this scope.");
        await Assert.That(NativeSerialization.Serialize(fixture.Database.Database.InspectMessage("root", lanes[1], "message")!)
            .SequenceEqual(beforeDenied)).IsTrue();
        var state = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var position = fixture.Database.Store.Position;
        var replay = await MultiLaneReceiveNativeFlow.ReceiveAsync(fixture, request, principal.Id);
        await Assert.That(replay.Payload.Span.SequenceEqual(reply.Payload.Span)).IsTrue();
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store)).IsEquivalentTo(state, CollectionOrdering.Matching);
        await HealthyAsync();
    }

    [Test, Arguments(0), Arguments(1), Arguments(2), Arguments(3), Arguments(4), Arguments(5), Arguments(6), Arguments(7), Arguments(8)]
    public async Task InvalidGroupFailsBeforeAnyClaimAndHealthyFollowingRequestSucceeds(int kind)
    {
        var lanes = MultiLaneReceiveNativeFlow.Seed(fixture, 2);
        var first = new ReceiveRequest(Guid.NewGuid(), lanes[0]);
        var second = new ReceiveRequest(Guid.NewGuid(), lanes[1]);
        var id = Guid.NewGuid();
        ImmutableArray<ReceiveRequest> entries = kind switch
        {
            0 => [],
            1 => [first, second with { Lane = first.Lane }],
            2 => [first, second with { RequestId = first.RequestId }],
            3 => [first with { RequestId = id }, second],
            4 => [first with { MaxMessages = 100 }, second],
            5 => [first with { MaxBytes = fixture.Database.Database.Limits.MaxBatchBytes }, second],
            6 => [.. Enumerable.Range(0, 9).Select(index => new ReceiveRequest(Guid.NewGuid(),
                new QueueLaneRef(fixture.Database.Partition, "over-limit-" + index)))],
            7 => [first with { MaxMessages = 0 }, second],
            _ => [first with { LeaseSeconds = 0 }, second]
        };
        var state = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var position = fixture.Database.Store.Position;
        var rejected = await MultiLaneReceiveNativeFlow.ReceiveAsync(fixture, new(id, entries));
        await Assert.That(rejected.Error).IsEqualTo(kind is >= 4 and <= 6 ? ErrorCode.BudgetExceeded : ErrorCode.Validation);
        await Assert.That(rejected.SafeDetail).IsEqualTo(kind is >= 4 and <= 6 ? MultiLaneReceiveAdmission.Exceeded : MultiLaneReceiveAdmission.Invalid);
        await Assert.That(rejected.Payload.IsEmpty).IsTrue();
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store)).IsEquivalentTo(state, CollectionOrdering.Matching);
        await HealthyAsync();
    }

    [Test]
    public async Task PrecancelledGroupPreservesFullCanonicalStateAndHealthyFollowup()
    {
        var lanes = MultiLaneReceiveNativeFlow.Seed(fixture, 2);
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(), [.. lanes.Select(lane => new ReceiveRequest(Guid.NewGuid(), lane))]);
        var state = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var position = fixture.Database.Store.Position;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            MultiLaneReceiveNativeFlow.ReceiveAsync(fixture, request, token: cancellation.Token));
        await Assert.That(failure!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store)).IsEquivalentTo(state, CollectionOrdering.Matching);
        await HealthyAsync();
    }

    private async Task HealthyAsync()
    {
        var lane = MultiLaneReceiveNativeFlow.Seed(fixture, 1)[0];
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(), [new(Guid.NewGuid(), lane)]);
        var reply = await MultiLaneReceiveNativeFlow.ReceiveAsync(fixture, request);
        await Assert.That(reply.Error).IsNull().Because(MultiLaneReceiveFailureObservation.Summary(fixture));
        var result = MultiLaneReceiveNativeFlow.Value(reply);
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(result.Outcomes.Length).IsEqualTo(1);
        await MultiLaneReceiveNativeFlow.CommittedAsync(result.Outcomes[0], request.Requests[0]);
        await Assert.That(fixture.Database.Database.InspectMessage("root", lane, "message")!.Metadata.State)
            .IsEqualTo(MessageState.Leased);
    }
}
