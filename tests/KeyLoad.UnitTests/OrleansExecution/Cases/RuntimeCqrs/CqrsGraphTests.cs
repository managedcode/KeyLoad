using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.OrleansExecution;

[CqrsDataSource]
internal sealed class CqrsGraphTests(CqrsClusterFixture fixture)
{
    [Test]
    public async Task AllowedMethodRetainsGraphCallerAcrossStartedAndAwait()
    {
        var requestId = CqrsRequestIds.Allowed;
        var source = fixture.GrainFactory.GetGrain<ICqrsStreamGrain>(requestId);
        var chunks = await CqrsTestSupport.ReadAllAsync(
            source.AllowedAsync(requestId, CancellationToken.None));

        await CqrsTestSupport.AssertTerminalSequenceAsync(chunks, CqrsStreamChunkKind.Completed);
        await Assert.That(chunks.Count).IsEqualTo(CqrsProtocol.ExpectedCompletedChunks);
        await Assert.That(chunks[0].Kind).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(chunks[1].Kind).IsEqualTo(CqrsStreamChunkKind.Progress);
        await Assert.That(chunks[2].TryGetResult(out var result)).IsTrue();
        await Assert.That(result).IsNotNull();
        var resultValue = result!;
        await Assert.That(resultValue.RequestId).IsEqualTo(requestId);
        await Assert.That(resultValue.Observation).IsNotNull();
        var leafObservation = resultValue.Observation!;
        await Assert.That(leafObservation.RequestId).IsEqualTo(requestId);
        await Assert.That(leafObservation.TargetInterface).IsEqualTo(typeof(ICqrsLeafGrain).FullName);
        await Assert.That(leafObservation.TargetMethod).IsEqualTo(nameof(ICqrsLeafGrain.ObserveAsync));
        await Assert.That(leafObservation.ObservedSource).IsEqualTo(typeof(ICqrsStreamGrain).FullName);
        await Assert.That(leafObservation.ObservedTarget).IsEqualTo(typeof(ICqrsLeafGrain).FullName);
        await Assert.That(leafObservation.ObservedSourceMethod).IsEqualTo(nameof(ICqrsStreamGrain.AllowedAsync));
        await Assert.That(leafObservation.ObservedTargetMethod).IsEqualTo(nameof(ICqrsLeafGrain.ObserveAsync));
        var streamId = fixture.GrainFactory.GetGrain<ICqrsStreamGrain>(requestId).GetGrainId().ToString();
        var leafId = fixture.GrainFactory.GetGrain<ICqrsLeafGrain>(requestId).GetGrainId().ToString();
        await Assert.That(leafObservation.OutgoingSourceId).IsEqualTo(streamId);
        await Assert.That(leafObservation.OutgoingTargetId).IsEqualTo(leafId);
        await Assert.That(leafObservation.IncomingSourceId).IsEqualTo(streamId);
        await Assert.That(leafObservation.IncomingTargetId).IsEqualTo(leafId);
        await Assert.That(leafObservation.LeafPrimaryKey).IsEqualTo(requestId.ToString());

        var observed = await CqrsTestSupport.WaitForEventAsync(
            fixture.GrainFactory, requestId, CqrsProtocol.Settled);
        await Assert.That(observed.Settled).IsTrue();
        await Assert.That(observed.SettlementCount).IsEqualTo(CqrsProtocol.ExpectedSettlementCount);
        await Assert.That(observed.HandlerOutcomeReturned).IsTrue();
        await Assert.That(observed.HandlerOutcomeSucceeded).IsTrue();
        await Assert.That(observed.Observation).IsEqualTo(leafObservation);
    }

    [Test]
    public async Task MissingMethodSpecificTransitionProducesFailedChunk()
    {
        var requestId = CqrsRequestIds.Denied;
        var source = fixture.GrainFactory.GetGrain<ICqrsStreamGrain>(requestId);
        var chunks = await CqrsTestSupport.ReadAllAsync(
            source.DeniedAsync(requestId, CancellationToken.None));

        await CqrsTestSupport.AssertTerminalSequenceAsync(chunks, CqrsStreamChunkKind.Failed);
        await Assert.That(chunks.Count).IsEqualTo(CqrsProtocol.ExpectedDeniedChunks);
        await Assert.That(chunks[0].Kind).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(chunks[1].TryGetProblem(out var problem)).IsTrue();
        await Assert.That(problem).IsNotNull();
        var failure = problem!;
        await Assert.That(failure.Title).IsEqualTo(CqrsProtocol.GraphDenialExceptionTitle);
        await Assert.That(failure.StatusCode).IsEqualTo(CqrsProtocol.GraphDenialStatus);

        var observed = await CqrsTestSupport.WaitForEventAsync(
            fixture.GrainFactory, requestId, CqrsProtocol.Settled);
        await Assert.That(observed.Events).Contains(CqrsProtocol.LeafCallAttempted);
        await Assert.That(observed.Observation).IsNull();
        await Assert.That(observed.Settled).IsTrue();
        await Assert.That(observed.CancellationObserved).IsFalse();
        await Assert.That(observed.SettlementCount).IsEqualTo(CqrsProtocol.ExpectedSettlementCount);
        await Assert.That(observed.HandlerOutcomeReturned).IsFalse();
    }
}
