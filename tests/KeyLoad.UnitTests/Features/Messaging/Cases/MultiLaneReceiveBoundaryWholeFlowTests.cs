using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.ClusterRouting;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

/// <summary>Real post-commit parent expiry and post-submit native byte-bound uncertainty.</summary>
[NotInParallel]
internal sealed class MultiLaneReceiveBoundaryWholeFlowTests
{
    [Test]
    public async Task OriginalParentExpiryRetainsCommittedLaneAndUndispatchedSuffixWithHealthyFollowup()
    {
        var clock = new MultiLaneReceiveExpiryClock();
        await using var fixture = new RequestCqrsClusterFixture(clock);
        await fixture.InitializeAsync();
        var lanes = MultiLaneReceiveNativeFlow.Seed(fixture, 2);
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(),
            [new(Guid.NewGuid(), lanes[0]), new(Guid.NewGuid(), lanes[1])]);
        var untouched = NativeSerialization.Serialize(fixture.Database.Database.InspectMessage("root", lanes[1], "message")!);
        var cut = fixture.Database.Store.Position;
        clock.Arm(() => fixture.Database.Store.Position, cut);
        var reply = await MultiLaneReceiveNativeFlow.ReceiveAsync(fixture, request);
        await Assert.That(reply.Error).IsNull();
        var result = MultiLaneReceiveNativeFlow.Value(reply);
        await Assert.That(result.Outcomes.Length).IsEqualTo(2);
        await MultiLaneReceiveNativeFlow.CommittedAsync(result.Outcomes[0], request.Requests[0]);
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
        await Assert.That(NativeSerialization.Serialize(fixture.Database.Database.InspectMessage("root", lanes[1], "message")!)
            .SequenceEqual(untouched)).IsTrue();
        var persisted = OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Database.Partition,
            "root", request.Requests[0].RequestId)!.Get<ReceiveResult>();
        await Assert.That(NativeSerialization.Serialize(persisted).SequenceEqual(NativeSerialization.Serialize(result.Outcomes[0].Result!))).IsTrue();
        var state = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var position = fixture.Database.Store.Position;
        var retry = new MultiLaneReceiveRequest(Guid.NewGuid(), [request.Requests[0]]);
        var recovered = await MultiLaneReceiveNativeFlow.ReceiveAsync(fixture, retry);
        await Assert.That(recovered.Error).IsNull();
        var expired = MultiLaneReceiveNativeFlow.Value(recovered);
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
        await Assert.That(NativeSerialization.Serialize(OutcomeStoreOracle.ReadPartition(fixture.Database.Store,
            fixture.Database.Partition, "root", request.Requests[0].RequestId)!.Get<ReceiveResult>())
            .SequenceEqual(NativeSerialization.Serialize(persisted))).IsTrue();
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store)).IsEquivalentTo(state, CollectionOrdering.Matching);
        await HealthyAsync(fixture);
    }

    [Test]
    public async Task SubmittedClaimReplyByteFailureIsUnknownAndNeverClaimsLaterLaneOrChangesOnReplay()
    {
        const int replyCeiling = 1024;
        await using var fixture = new RequestCqrsClusterFixture(routing: new GrainRoutingOptions
        { MaximumReplyBytes = replyCeiling, InitialReplyBufferBytes = replyCeiling });
        await fixture.InitializeAsync();
        var prefix = "large-receive-" + Guid.NewGuid().ToString("N");
        var lanes = new[] { new QueueLaneRef(fixture.Database.Partition, prefix + "-first"),
            new QueueLaneRef(fixture.Database.Partition, prefix + "-second") };
        foreach (var lane in lanes)
        { fixture.Database.Configure(lane.Queue, ResourceKind.WorkQueue); }
        var payload = "{\"value\":\"" + new string('a', 2048) + "\"}";
        fixture.Database.Commit(new EnqueueMessage(lanes[0].Queue, "large", payload, "{\"kind\":\"large\"}"),
            new EnqueueMessage(lanes[1].Queue, "untouched", "{\"value\":2}"));
        var before = NativeSerialization.Serialize(fixture.Database.Database.InspectMessage("root", lanes[1], "untouched")!);
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(),
            [new(Guid.NewGuid(), lanes[0]), new(Guid.NewGuid(), lanes[1])]);
        var cut = fixture.Database.Store.Position;
        var reply = await MultiLaneReceiveNativeFlow.ReceiveAsync(fixture, request);
        await Assert.That(reply.Error).IsNull();
        var result = MultiLaneReceiveNativeFlow.Value(reply);
        await Assert.That(result.Outcomes.Length).IsEqualTo(2);
        await Assert.That(result.Outcomes[0].Status).IsEqualTo(QueueLaneReceiveStatus.Unknown);
        await Assert.That(result.Outcomes[0].Error).IsEqualTo(ErrorCode.UnknownWriteOutcome);
        await Assert.That(result.Outcomes[0].SafeDetail).IsEqualTo("The write outcome is unknown. Retry the same command ID.");
        await Assert.That(result.Outcomes[0].Result).IsNull();
        await Assert.That(result.Outcomes[1].Status).IsEqualTo(QueueLaneReceiveStatus.NotAttempted);
        await Assert.That(result.Outcomes[1].Result).IsNull();
        await Assert.That(result.Outcomes[1].Error).IsNull();
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(cut + 1);
        var original = OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Database.Partition,
            "root", request.Requests[0].RequestId)!.Get<ReceiveResult>();
        var delivery = await Assert.That(original.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo("large");
        await Assert.That(delivery.PayloadJson).IsEqualTo(payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo("{\"kind\":\"large\"}");
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(NativeSerialization.Serialize(new GrainValue(original)).Length).IsGreaterThan(replyCeiling);
        await Assert.That(OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Database.Partition,
            "root", request.Requests[1].RequestId)).IsNull();
        await Assert.That(NativeSerialization.Serialize(fixture.Database.Database.InspectMessage("root", lanes[1], "untouched")!)
            .SequenceEqual(before)).IsTrue();
        var state = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var position = fixture.Database.Store.Position;
        var retry = await MultiLaneReceiveNativeFlow.ReceiveAsync(fixture, request);
        await Assert.That(retry.Payload.Span.SequenceEqual(reply.Payload.Span)).IsTrue();
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store)).IsEquivalentTo(state, CollectionOrdering.Matching);
        await HealthyAsync(fixture);
    }

    private static async Task HealthyAsync(RequestCqrsClusterFixture fixture)
    {
        var lane = MultiLaneReceiveNativeFlow.Seed(fixture, 1)[0];
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(), [new(Guid.NewGuid(), lane)]);
        var reply = await MultiLaneReceiveNativeFlow.ReceiveAsync(fixture, request);
        await Assert.That(reply.Error).IsNull();
        var result = MultiLaneReceiveNativeFlow.Value(reply);
        await Assert.That(result.StopError).IsNull();
        await Assert.That(result.Outcomes.Length).IsEqualTo(1);
        await MultiLaneReceiveNativeFlow.CommittedAsync(result.Outcomes[0], request.Requests[0]);
    }
}
