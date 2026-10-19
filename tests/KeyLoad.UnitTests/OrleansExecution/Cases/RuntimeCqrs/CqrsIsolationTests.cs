using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

[CqrsDataSource]
internal sealed class CqrsIsolationTests(CqrsClusterFixture fixture)
{
    [Test]
    public async Task ConcurrentStreamsKeepRequestCallerAndTerminalIdentitySeparate()
    {
        var firstId = CqrsRequestIds.ConcurrentOne;
        var secondId = CqrsRequestIds.ConcurrentTwo;
        var firstStream = fixture.GrainFactory.GetGrain<ICqrsStreamGrain>(firstId)
            .AllowedAsync(firstId, CancellationToken.None);
        var secondStream = fixture.GrainFactory.GetGrain<ICqrsStreamGrain>(secondId)
            .AllowedAsync(secondId, CancellationToken.None);

        var outcomes = await Task.WhenAll(
            CqrsTestSupport.ReadAllAsync(firstStream),
            CqrsTestSupport.ReadAllAsync(secondStream));
        var first = outcomes[0];
        var second = outcomes[1];

        await CqrsTestSupport.AssertTerminalSequenceAsync(first, CqrsStreamChunkKind.Completed);
        await CqrsTestSupport.AssertTerminalSequenceAsync(second, CqrsStreamChunkKind.Completed);
        await Assert.That(first[2].TryGetResult(out var firstResult)).IsTrue();
        await Assert.That(second[2].TryGetResult(out var secondResult)).IsTrue();
        var firstValue = firstResult!;
        var secondValue = secondResult!;
        await Assert.That(firstValue.RequestId).IsEqualTo(firstId);
        await Assert.That(secondValue.RequestId).IsEqualTo(secondId);
        var firstObservation = firstValue.Observation!;
        var secondObservation = secondValue.Observation!;
        await RuntimeAssertIdentityAsync(firstObservation, firstId);
        await RuntimeAssertIdentityAsync(secondObservation, secondId);
        await Assert.That(firstObservation.OutgoingSourceId).IsNotEqualTo(secondObservation.OutgoingSourceId);
        await Assert.That(firstObservation).IsNotEqualTo(secondObservation);

        var firstState = await CqrsTestSupport.WaitForEventAsync(
            fixture.GrainFactory, firstId, CqrsProtocol.Settled);
        var secondState = await CqrsTestSupport.WaitForEventAsync(
            fixture.GrainFactory, secondId, CqrsProtocol.Settled);
        await Assert.That(firstState.RequestId).IsEqualTo(firstId);
        await Assert.That(secondState.RequestId).IsEqualTo(secondId);
        await Assert.That(firstState.Observation).IsEqualTo(firstObservation);
        await Assert.That(secondState.Observation).IsEqualTo(secondObservation);
        await Assert.That(firstState.SettlementCount).IsEqualTo(CqrsProtocol.ExpectedSettlementCount);
        await Assert.That(secondState.SettlementCount).IsEqualTo(CqrsProtocol.ExpectedSettlementCount);
        await Assert.That(firstState.HandlerOutcomeSucceeded).IsTrue();
        await Assert.That(secondState.HandlerOutcomeSucceeded).IsTrue();
    }

    private async Task RuntimeAssertIdentityAsync(CqrsLeafObservation observation, Guid requestId)
    {
        await Assert.That(observation.RequestId).IsEqualTo(requestId);
        await Assert.That(observation.ObservedSource).IsEqualTo(typeof(ICqrsStreamGrain).FullName);
        await Assert.That(observation.ObservedTarget).IsEqualTo(typeof(ICqrsLeafGrain).FullName);
        await Assert.That(observation.ObservedSourceMethod).IsEqualTo(nameof(ICqrsStreamGrain.AllowedAsync));
        await Assert.That(observation.ObservedTargetMethod).IsEqualTo(nameof(ICqrsLeafGrain.ObserveAsync));
        await Assert.That(observation.TargetMethod).IsEqualTo(nameof(ICqrsLeafGrain.ObserveAsync));
        await Assert.That(observation.LeafPrimaryKey).IsEqualTo(requestId.ToString());
        var sourceId = fixture.GrainFactory.GetGrain<ICqrsStreamGrain>(requestId).GetGrainId().ToString();
        var leafId = fixture.GrainFactory.GetGrain<ICqrsLeafGrain>(requestId).GetGrainId().ToString();
        await Assert.That(observation.OutgoingSourceId).IsEqualTo(sourceId);
        await Assert.That(observation.OutgoingTargetId).IsEqualTo(leafId);
        await Assert.That(observation.IncomingSourceId).IsEqualTo(sourceId);
        await Assert.That(observation.IncomingTargetId).IsEqualTo(leafId);
    }
}
