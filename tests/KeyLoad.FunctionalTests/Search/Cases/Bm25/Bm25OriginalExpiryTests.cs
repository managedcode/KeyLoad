namespace KeyLoad.FunctionalTests.Search;

internal sealed class Bm25OriginalExpiryTests
{
    [Test]
    public Task OriginalInnerExpiryRefusesFirstApplyButPreservesAdmittedReceiptThenColdReplayAndFreshHealthyPublication()
        => Bm25OriginalPhaseRecoveryOwner.RunAsync(Bm25OriginalExpiryTrial.RunAsync);
}
