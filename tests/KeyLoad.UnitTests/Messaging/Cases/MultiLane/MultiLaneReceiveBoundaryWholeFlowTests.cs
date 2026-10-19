using KeyLoad.Clustering;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

/// <summary>Real post-commit parent expiry and post-submit execution byte-bound uncertainty.</summary>
[NotInParallel]
internal sealed class MultiLaneReceiveBoundaryWholeFlowTests
{
    [Test]
    public async Task OriginalParentExpiryRetainsCommittedLaneAndUndispatchedSuffixWithHealthyFollowup()
    {
        var clock = new MultiLaneReceiveExpiryClock();
        await using var fixture = new RequestCqrsHostedRuntimeFixture(clock);
        await fixture.InitializeAsync();
        var lanes = MultiLaneReceiveFlow.Seed(fixture, 2);
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(),
            [new(Guid.NewGuid(), lanes[0]), new(Guid.NewGuid(), lanes[1])]);
        var untouched = OrleansSerialization.Serialize(fixture.Database.Database.InspectMessage("root", lanes[1], "message")!);
        var cut = fixture.Database.Store.Position;
        clock.Arm(() => fixture.Database.Store.Position, cut);
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

    [Test]
    public async Task SubmittedClaimReplyByteFailureIsUnknownAndNeverClaimsLaterLaneOrChangesOnReplay()
    {
        const int replyCeiling = 1024;
        await using var fixture = new RequestCqrsHostedRuntimeFixture(routing: new GrainRoutingOptions
        {
            MaximumReplyBytes = replyCeiling,
            InitialReplyBufferBytes = replyCeiling
        });
        await fixture.InitializeAsync();
        var prefix = "large-receive-" + Guid.NewGuid().ToString("N");
        var lanes = new[] { new QueueLaneRef(fixture.Database.Partition, prefix + "-first"),
            new QueueLaneRef(fixture.Database.Partition, prefix + "-second") };
        foreach (var lane in lanes)
        {
            fixture.Database.Configure(lane.Queue, ResourceKind.WorkQueue);
        }
        var payload = "{\"value\":\"" + new string('a', 2048) + "\"}";
        fixture.Database.Commit(new EnqueueMessage(lanes[0].Queue, "large", payload, "{\"kind\":\"large\"}"),
            new EnqueueMessage(lanes[1].Queue, "untouched", "{\"value\":2}"));
        var before = OrleansSerialization.Serialize(fixture.Database.Database.InspectMessage("root", lanes[1], "untouched")!);
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(),
            [new(Guid.NewGuid(), lanes[0]), new(Guid.NewGuid(), lanes[1])]);
        var cut = fixture.Database.Store.Position;
        var reply = await MultiLaneReceiveFlow.ReceiveAsync(fixture, request);
        await Assert.That(reply.Error).IsNull().Because(MultiLaneReceiveFailureObservation.Summary(fixture));
        var result = MultiLaneReceiveFlow.Value(reply);
        await Assert.That(result.Outcomes.Length).IsEqualTo(2);
        await Assert.That(result.Outcomes[0].Status).IsEqualTo(QueueLaneReceiveStatus.Unknown);
        await Assert.That(result.Outcomes[0].Error).IsEqualTo(ErrorCode.UnknownWriteOutcome);
        await Assert.That(result.Outcomes[0].SafeDetail).IsEqualTo("The write outcome is unknown. Retry the same command ID.");
        await Assert.That(result.Outcomes[0].Result).IsNull();
        await Assert.That(result.Outcomes[1].Status).IsEqualTo(QueueLaneReceiveStatus.NotAttempted);
        await Assert.That(result.Outcomes[1].Result).IsNull();
        await Assert.That(result.Outcomes[1].Error).IsNull();
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(cut + 1);
        var outcome = OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Database.Partition,
            "root", request.Requests[0].RequestId)
            ?? throw new InvalidOperationException("The submitted first-lane outcome is absent.");
        var original = outcome.Get<ReceiveResult>();
        var delivery = await Assert.That(original.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo("large");
        await Assert.That(delivery.PayloadJson).IsEqualTo(payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo("{\"kind\":\"large\"}");
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(OrleansSerialization.Serialize(new GrainValue(original)).Length).IsGreaterThan(replyCeiling);
        await Assert.That(OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Database.Partition,
            "root", request.Requests[1].RequestId)).IsNull();
        await Assert.That(OrleansSerialization.Serialize(fixture.Database.Database.InspectMessage("root", lanes[1], "untouched")!)
            .SequenceEqual(before)).IsTrue();
        var state = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var position = fixture.Database.Store.Position;
        var retry = await MultiLaneReceiveFlow.ReceiveAsync(fixture, request);
        await Assert.That(retry.Payload.Span.SequenceEqual(reply.Payload.Span)).IsTrue();
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
