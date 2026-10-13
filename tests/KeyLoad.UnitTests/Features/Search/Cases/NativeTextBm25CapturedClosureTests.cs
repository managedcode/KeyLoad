namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextBm25CapturedClosureTests
{
    [Test]
    public Task AbsentAndPublishedCorpusHeadsPreserveCompleteCapturedPagesAndOriginalReceipt()
        => Bm25OriginalPhaseRecoveryOwner.RunAsync(NativeTextBm25CapturedClosureTrial.RunAsync);
}
