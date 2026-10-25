using ManagedCode.Communication.CQRS;

namespace KeyLoad.FunctionalTests.OrleansExecution;

[CqrsDataSource]
internal sealed class CqrsTerminalTests(CqrsClusterFixture fixture)
{
    [Test]
    public async Task DomainFailureAfterProgressProducesOneSafeTerminalChunk()
    {
        var requestId = CqrsRequestIds.Failure;
        var source = fixture.GrainFactory.GetGrain<ICqrsStreamGrain>(requestId);
        var chunks = await CqrsTestSupport.ReadAllAsync(
            source.FailAfterProgressAsync(requestId, CancellationToken.None));

        await CqrsTestSupport.AssertTerminalSequenceAsync(chunks, CqrsStreamChunkKind.Failed);
        await Assert.That(chunks.Count).IsEqualTo(CqrsProtocol.ExpectedFailureChunks);
        await Assert.That(chunks[0].Kind).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(chunks[1].Kind).IsEqualTo(CqrsStreamChunkKind.Progress);
        await Assert.That(chunks[1].TryGetProgress(out var progress)).IsTrue();
        var progressValue = progress!;
        await Assert.That(progressValue.RequestId).IsEqualTo(requestId);
        await Assert.That(progressValue.Stage).IsEqualTo(CqrsProtocol.ProgressOne);
        await Assert.That(chunks[2].TryGetProblem(out var problem)).IsTrue();
        var failure = problem!;
        await Assert.That(failure.Title).IsEqualTo(CqrsProtocol.FailureTitle);
        await Assert.That(failure.Detail).IsEqualTo(CqrsProtocol.FailureDetail);
        await Assert.That(failure.StatusCode).IsEqualTo(CqrsProtocol.FailureStatus);

        var settled = await CqrsTestSupport.WaitForEventAsync(
            fixture.GrainFactory, requestId, CqrsProtocol.Settled);
        await Assert.That(settled.Settled).IsTrue();
        await Assert.That(settled.CancellationObserved).IsFalse();
        await Assert.That(settled.SettlementCount).IsEqualTo(CqrsProtocol.ExpectedSettlementCount);
        await Assert.That(settled.Events).Contains(CqrsProtocol.Started);
        await Assert.That(settled.Events).Contains(CqrsProtocol.ProgressOne);
        await Assert.That(settled.HandlerOutcomeReturned).IsTrue();
        await Assert.That(settled.HandlerOutcomeSucceeded).IsFalse();
    }
}
