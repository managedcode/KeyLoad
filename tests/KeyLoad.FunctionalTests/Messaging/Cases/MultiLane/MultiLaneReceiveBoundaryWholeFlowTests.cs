using KeyLoad.Clustering;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Messaging;

/// <summary>Real post-commit parent expiry and post-submit execution byte-bound uncertainty.</summary>
[NotInParallel]
internal sealed class MultiLaneReceiveBoundaryWholeFlowTests
{
    [Test]
    public async Task OriginalParentExpiryRetainsCommittedLaneAndUndispatchedSuffixWithHealthyFollowup()
    {
        var clock = new MultiLaneReceiveExpiryClock();
        await using var fixture = new RequestCqrsHostedRuntimeFixture(clock, observer: clock);
        await fixture.InitializeAsync();
        var lanes = MultiLaneReceiveFlow.Seed(fixture, 2);
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(),
            [new(Guid.NewGuid(), lanes[0]), new(Guid.NewGuid(), lanes[1])]);
        var untouched = OrleansSerialization.Serialize(fixture.Database.Database.InspectMessage("root", lanes[1], "message")!);
        var cut = fixture.Database.Store.Position;
        clock.Arm(() => fixture.Database.Store.Position, cut, request.Requests[0].RequestId);
        var reply = await MultiLaneReceiveFlow.ReceiveAsync(fixture, request);
        await Assert.That(reply.Error).IsNull().Because(MultiLaneReceiveFailureObservation.Summary(fixture));
        var result = MultiLaneReceiveFlow.Value(reply);
        await Assert.That(result.Outcomes.Length).IsEqualTo(2);
        await MultiLaneReceiveFlow.CommittedAsync(result.Outcomes[0], request.Requests[0]);
        await Assert.That(result.StopError).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(result.SafeDetail).IsEqualTo(GrainRoutingProtocol.InvalidRequest);
        await Assert.That(result.Outcomes[1].Status).IsEqualTo(QueueLaneReceiveStatus.NotAttempted);
        await Assert.That(result.Outcomes[1].RequestId).IsEqualTo(request.Requests[1].RequestId);
        await Assert.That(result.Outcomes[1].Lane).IsEqualTo(lanes[1]);
        await Assert.That(result.Outcomes[1].Result).IsNull();
        await Assert.That(result.Outcomes[1].Error).IsNull();
        await Assert.That(result.Outcomes[1].SafeDetail).IsNull();
        await Assert.That(clock.ObservedPosition).IsEqualTo(cut + 1);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(cut + 1);
        await Assert.That(OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Database.Partition,
            "root", request.Requests[1].RequestId)).IsNull();
        await Assert.That(OrleansSerialization.Serialize(fixture.Database.Database.InspectMessage("root", lanes[1], "message")!)
            .SequenceEqual(untouched)).IsTrue();
        var persistedOutcome = OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Database.Partition,
            "root", request.Requests[0].RequestId)
            ?? throw new InvalidOperationException("The committed first-lane outcome is absent.");
        var persisted = persistedOutcome.Get<ReceiveResult>();
        await Assert.That(OrleansSerialization.Serialize(persisted).SequenceEqual(OrleansSerialization.Serialize(result.Outcomes[0].Result!))).IsTrue();
        var state = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var position = fixture.Database.Store.Position;
        var retry = new MultiLaneReceiveRequest(Guid.NewGuid(), [request.Requests[0]]);
        var recovered = await MultiLaneReceiveFlow.ReceiveAsync(fixture, retry);
        await Assert.That(recovered.Error).IsNull().Because(MultiLaneReceiveFailureObservation.Summary(fixture));
        var expired = MultiLaneReceiveFlow.Value(recovered);
        await Assert.That(expired.RequestId).IsEqualTo(retry.RequestId);
        await Assert.That(expired.Outcomes.Length).IsEqualTo(1);
        await Assert.That(expired.StopError).IsNull();
        await Assert.That(expired.SafeDetail).IsNull();
        await Assert.That(expired.Outcomes[0].RequestId).IsEqualTo(request.Requests[0].RequestId);
        await Assert.That(expired.Outcomes[0].Lane).IsEqualTo(lanes[0]);
        await Assert.That(expired.Outcomes[0].Status).IsEqualTo(QueueLaneReceiveStatus.Rejected);
        await Assert.That(expired.Outcomes[0].Error).IsEqualTo(ErrorCode.LeaseExpired);
        await Assert.That(expired.Outcomes[0].SafeDetail).IsEqualTo("The delivery lease has expired.");
        await Assert.That(expired.Outcomes[0].Result).IsNull();
        var retained = OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Database.Partition,
            "root", request.Requests[0].RequestId)
            ?? throw new InvalidOperationException("The original first-lane outcome is absent after replay.");
        await Assert.That(OrleansSerialization.Serialize(retained.Get<ReceiveResult>())
            .SequenceEqual(OrleansSerialization.Serialize(persisted))).IsTrue();
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store)).IsEquivalentTo(state, CollectionOrdering.Matching);
        await HealthyAsync(fixture);
    }

    private static async Task HealthyAsync(RequestCqrsHostedRuntimeFixture fixture)
    {
        var lane = MultiLaneReceiveFlow.Seed(fixture, 1)[0];
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(), [new(Guid.NewGuid(), lane)]);
        var reply = await MultiLaneReceiveFlow.ReceiveAsync(fixture, request);
        await Assert.That(reply.Error).IsNull().Because(MultiLaneReceiveFailureObservation.Summary(fixture));
        var result = MultiLaneReceiveFlow.Value(reply);
        await Assert.That(result.StopError).IsNull();
        await Assert.That(result.Outcomes.Length).IsEqualTo(1);
        await MultiLaneReceiveFlow.CommittedAsync(result.Outcomes[0], request.Requests[0]);
    }
}
