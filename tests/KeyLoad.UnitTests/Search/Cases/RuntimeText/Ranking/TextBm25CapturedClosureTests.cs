namespace KeyLoad.UnitTests.Search;

internal sealed class TextBm25CapturedClosureTests
{
    [Test]
    public Task AbsentAndPublishedCorpusHeadsPreserveCompleteCapturedPagesAndOriginalReceipt()
        => Bm25OriginalPhaseRecoveryOwner.RunAsync(TextBm25CapturedClosureTrial.RunAsync);
}
