using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NativeCqrsDataSource]
internal sealed class NativeCqrsTerminalTests(NativeCqrsClusterFixture fixture)
{
    [Test]
    public async Task DomainFailureAfterProgressProducesOneSafeTerminalChunk()
    {
        var requestId = NativeCqrsRequestIds.Failure;
        var source = fixture.GrainFactory.GetGrain<INativeCqrsStreamGrain>(requestId);
        var chunks = await NativeCqrsTestSupport.ReadAllAsync(
            source.FailAfterProgressAsync(requestId, CancellationToken.None));

        await NativeCqrsTestSupport.AssertTerminalSequenceAsync(chunks, CqrsStreamChunkKind.Failed);
        await Assert.That(chunks.Count).IsEqualTo(NativeCqrsProtocol.ExpectedFailureChunks);
        await Assert.That(chunks[0].Kind).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(chunks[1].Kind).IsEqualTo(CqrsStreamChunkKind.Progress);
        await Assert.That(chunks[1].TryGetProgress(out var progress)).IsTrue();
        var progressValue = progress!;
        await Assert.That(progressValue.RequestId).IsEqualTo(requestId);
        await Assert.That(progressValue.Stage).IsEqualTo(NativeCqrsProtocol.ProgressOne);
        await Assert.That(chunks[2].TryGetProblem(out var problem)).IsTrue();
        var failure = problem!;
        await Assert.That(failure.Title).IsEqualTo(NativeCqrsProtocol.FailureTitle);
        await Assert.That(failure.Detail).IsEqualTo(NativeCqrsProtocol.FailureDetail);
        await Assert.That(failure.StatusCode).IsEqualTo(NativeCqrsProtocol.FailureStatus);

        var settled = await NativeCqrsTestSupport.WaitForEventAsync(
            fixture.GrainFactory, requestId, NativeCqrsProtocol.Settled);
        await Assert.That(settled.Settled).IsTrue();
        await Assert.That(settled.CancellationObserved).IsFalse();
        await Assert.That(settled.SettlementCount).IsEqualTo(NativeCqrsProtocol.ExpectedSettlementCount);
        await Assert.That(settled.Events).Contains(NativeCqrsProtocol.Started);
        await Assert.That(settled.Events).Contains(NativeCqrsProtocol.ProgressOne);
        await Assert.That(settled.HandlerOutcomeReturned).IsTrue();
        await Assert.That(settled.HandlerOutcomeSucceeded).IsFalse();
    }
}
