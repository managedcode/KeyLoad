namespace KeyLoad.UnitTests.Search;

internal sealed class Bm25OriginalExpiryTests
{
    [Test]
    public Task OriginalInnerExpiryRefusesFirstApplyButPreservesAdmittedReceiptThenColdReplayAndFreshHealthyPublication()
        => Bm25OriginalPhaseRecoveryOwner.RunAsync(Bm25OriginalExpiryTrial.RunAsync);
}
