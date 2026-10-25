namespace KeyLoad.FunctionalTests.Search;

internal sealed class Bm25OriginalPhaseRecoveryTests
{
    [Test]
    public Task OriginalPhaseReceiptColdRecoveryHonorsCurrentAdminAndSubjectEpochsThenFreshPublicationIsHealthy()
        => Bm25OriginalPhaseRecoveryOwner.RunAsync(Bm25OriginalPhaseRecoveryTrial.RunAsync);
}
