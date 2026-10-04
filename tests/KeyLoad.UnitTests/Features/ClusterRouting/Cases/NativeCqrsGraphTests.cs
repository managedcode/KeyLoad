using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NativeCqrsDataSource]
internal sealed class NativeCqrsGraphTests(NativeCqrsClusterFixture fixture)
{
    [Test]
    public async Task AllowedMethodRetainsGraphCallerAcrossStartedAndAwait()
    {
        var requestId = NativeCqrsRequestIds.Allowed;
        var source = fixture.GrainFactory.GetGrain<INativeCqrsStreamGrain>(requestId);
        var chunks = await NativeCqrsTestSupport.ReadAllAsync(
            source.AllowedAsync(requestId, CancellationToken.None));

        await NativeCqrsTestSupport.AssertTerminalSequenceAsync(chunks, CqrsStreamChunkKind.Completed);
        await Assert.That(chunks.Count).IsEqualTo(NativeCqrsProtocol.ExpectedCompletedChunks);
        await Assert.That(chunks[0].Kind).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(chunks[1].Kind).IsEqualTo(CqrsStreamChunkKind.Progress);
        await Assert.That(chunks[2].TryGetResult(out var result)).IsTrue();
        await Assert.That(result).IsNotNull();
        var resultValue = result!;
        await Assert.That(resultValue.RequestId).IsEqualTo(requestId);
        await Assert.That(resultValue.Observation).IsNotNull();
        var leafObservation = resultValue.Observation!;
        await Assert.That(leafObservation.RequestId).IsEqualTo(requestId);
        await Assert.That(leafObservation.TargetInterface).IsEqualTo(typeof(INativeCqrsLeafGrain).FullName);
        await Assert.That(leafObservation.TargetMethod).IsEqualTo(nameof(INativeCqrsLeafGrain.ObserveAsync));
        await Assert.That(leafObservation.ObservedSource).IsEqualTo(typeof(INativeCqrsStreamGrain).FullName);
        await Assert.That(leafObservation.ObservedTarget).IsEqualTo(typeof(INativeCqrsLeafGrain).FullName);
        await Assert.That(leafObservation.ObservedSourceMethod).IsEqualTo(nameof(INativeCqrsStreamGrain.AllowedAsync));
        await Assert.That(leafObservation.ObservedTargetMethod).IsEqualTo(nameof(INativeCqrsLeafGrain.ObserveAsync));
        var streamId = fixture.GrainFactory.GetGrain<INativeCqrsStreamGrain>(requestId).GetGrainId().ToString();
        var leafId = fixture.GrainFactory.GetGrain<INativeCqrsLeafGrain>(requestId).GetGrainId().ToString();
        await Assert.That(leafObservation.OutgoingSourceId).IsEqualTo(streamId);
        await Assert.That(leafObservation.OutgoingTargetId).IsEqualTo(leafId);
        await Assert.That(leafObservation.IncomingSourceId).IsEqualTo(streamId);
        await Assert.That(leafObservation.IncomingTargetId).IsEqualTo(leafId);
        await Assert.That(leafObservation.LeafPrimaryKey).IsEqualTo(requestId.ToString());

        var observed = await NativeCqrsTestSupport.WaitForEventAsync(
            fixture.GrainFactory, requestId, NativeCqrsProtocol.Settled);
        await Assert.That(observed.Settled).IsTrue();
        await Assert.That(observed.SettlementCount).IsEqualTo(NativeCqrsProtocol.ExpectedSettlementCount);
        await Assert.That(observed.HandlerOutcomeReturned).IsTrue();
        await Assert.That(observed.HandlerOutcomeSucceeded).IsTrue();
        await Assert.That(observed.Observation).IsEqualTo(leafObservation);
    }

    [Test]
    public async Task MissingMethodSpecificTransitionProducesNativeFailedChunk()
    {
        var requestId = NativeCqrsRequestIds.Denied;
        var source = fixture.GrainFactory.GetGrain<INativeCqrsStreamGrain>(requestId);
        var chunks = await NativeCqrsTestSupport.ReadAllAsync(
            source.DeniedAsync(requestId, CancellationToken.None));

        await NativeCqrsTestSupport.AssertTerminalSequenceAsync(chunks, CqrsStreamChunkKind.Failed);
        await Assert.That(chunks.Count).IsEqualTo(NativeCqrsProtocol.ExpectedDeniedChunks);
        await Assert.That(chunks[0].Kind).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(chunks[1].TryGetProblem(out var problem)).IsTrue();
        await Assert.That(problem).IsNotNull();
        var failure = problem!;
        await Assert.That(failure.Title).IsEqualTo(NativeCqrsProtocol.GraphDenialExceptionTitle);
        await Assert.That(failure.StatusCode).IsEqualTo(NativeCqrsProtocol.GraphDenialStatus);

        var observed = await NativeCqrsTestSupport.WaitForEventAsync(
            fixture.GrainFactory, requestId, NativeCqrsProtocol.Settled);
        await Assert.That(observed.Events).Contains(NativeCqrsProtocol.LeafCallAttempted);
        await Assert.That(observed.Observation).IsNull();
        await Assert.That(observed.Settled).IsTrue();
        await Assert.That(observed.CancellationObserved).IsFalse();
        await Assert.That(observed.SettlementCount).IsEqualTo(NativeCqrsProtocol.ExpectedSettlementCount);
        await Assert.That(observed.HandlerOutcomeReturned).IsFalse();
    }
}
