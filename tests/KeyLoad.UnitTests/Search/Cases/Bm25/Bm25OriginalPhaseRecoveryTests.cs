namespace KeyLoad.UnitTests.Features.Search;

internal sealed class Bm25OriginalPhaseRecoveryTests
{
    [Test]
    public Task OriginalPhaseReceiptColdRecoveryHonorsCurrentAdminAndSubjectEpochsThenFreshPublicationIsHealthy()
        => Bm25OriginalPhaseRecoveryOwner.RunAsync(Bm25OriginalPhaseRecoveryTrial.RunAsync);
}
