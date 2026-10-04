using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NativeCqrsDataSource]
internal sealed class NativeCqrsIsolationTests(NativeCqrsClusterFixture fixture)
{
    [Test]
    public async Task ConcurrentStreamsKeepRequestCallerAndTerminalIdentitySeparate()
    {
        var firstId = NativeCqrsRequestIds.ConcurrentOne;
        var secondId = NativeCqrsRequestIds.ConcurrentTwo;
        var firstStream = fixture.GrainFactory.GetGrain<INativeCqrsStreamGrain>(firstId)
            .AllowedAsync(firstId, CancellationToken.None);
        var secondStream = fixture.GrainFactory.GetGrain<INativeCqrsStreamGrain>(secondId)
            .AllowedAsync(secondId, CancellationToken.None);

        var outcomes = await Task.WhenAll(
            NativeCqrsTestSupport.ReadAllAsync(firstStream),
            NativeCqrsTestSupport.ReadAllAsync(secondStream));
        var first = outcomes[0];
        var second = outcomes[1];

        await NativeCqrsTestSupport.AssertTerminalSequenceAsync(first, CqrsStreamChunkKind.Completed);
        await NativeCqrsTestSupport.AssertTerminalSequenceAsync(second, CqrsStreamChunkKind.Completed);
        await Assert.That(first[2].TryGetResult(out var firstResult)).IsTrue();
        await Assert.That(second[2].TryGetResult(out var secondResult)).IsTrue();
        var firstValue = firstResult!;
        var secondValue = secondResult!;
        await Assert.That(firstValue.RequestId).IsEqualTo(firstId);
        await Assert.That(secondValue.RequestId).IsEqualTo(secondId);
        var firstObservation = firstValue.Observation!;
        var secondObservation = secondValue.Observation!;
        await AssertNativeIdentityAsync(firstObservation, firstId);
        await AssertNativeIdentityAsync(secondObservation, secondId);
        await Assert.That(firstObservation.OutgoingSourceId).IsNotEqualTo(secondObservation.OutgoingSourceId);
        await Assert.That(firstObservation).IsNotEqualTo(secondObservation);

        var firstState = await NativeCqrsTestSupport.WaitForEventAsync(
            fixture.GrainFactory, firstId, NativeCqrsProtocol.Settled);
        var secondState = await NativeCqrsTestSupport.WaitForEventAsync(
            fixture.GrainFactory, secondId, NativeCqrsProtocol.Settled);
        await Assert.That(firstState.RequestId).IsEqualTo(firstId);
        await Assert.That(secondState.RequestId).IsEqualTo(secondId);
        await Assert.That(firstState.Observation).IsEqualTo(firstObservation);
        await Assert.That(secondState.Observation).IsEqualTo(secondObservation);
        await Assert.That(firstState.SettlementCount).IsEqualTo(NativeCqrsProtocol.ExpectedSettlementCount);
        await Assert.That(secondState.SettlementCount).IsEqualTo(NativeCqrsProtocol.ExpectedSettlementCount);
        await Assert.That(firstState.HandlerOutcomeSucceeded).IsTrue();
        await Assert.That(secondState.HandlerOutcomeSucceeded).IsTrue();
    }

    private async Task AssertNativeIdentityAsync(NativeCqrsLeafObservation observation, Guid requestId)
    {
        await Assert.That(observation.RequestId).IsEqualTo(requestId);
        await Assert.That(observation.ObservedSource).IsEqualTo(typeof(INativeCqrsStreamGrain).FullName);
        await Assert.That(observation.ObservedTarget).IsEqualTo(typeof(INativeCqrsLeafGrain).FullName);
        await Assert.That(observation.ObservedSourceMethod).IsEqualTo(nameof(INativeCqrsStreamGrain.AllowedAsync));
        await Assert.That(observation.ObservedTargetMethod).IsEqualTo(nameof(INativeCqrsLeafGrain.ObserveAsync));
        await Assert.That(observation.TargetMethod).IsEqualTo(nameof(INativeCqrsLeafGrain.ObserveAsync));
        await Assert.That(observation.LeafPrimaryKey).IsEqualTo(requestId.ToString());
        var sourceId = fixture.GrainFactory.GetGrain<INativeCqrsStreamGrain>(requestId).GetGrainId().ToString();
        var leafId = fixture.GrainFactory.GetGrain<INativeCqrsLeafGrain>(requestId).GetGrainId().ToString();
        await Assert.That(observation.OutgoingSourceId).IsEqualTo(sourceId);
        await Assert.That(observation.OutgoingTargetId).IsEqualTo(leafId);
        await Assert.That(observation.IncomingSourceId).IsEqualTo(sourceId);
        await Assert.That(observation.IncomingTargetId).IsEqualTo(leafId);
    }
}
