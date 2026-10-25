namespace KeyLoad.FunctionalTests.Search;

internal sealed class Bm25ColdInvalidationTests
{
    [Test]
    public Task OriginalPersistedHeadFallsBackAfterDocumentAndPolicyChangesAcrossSameOwnerColdReopens()
        => Bm25OriginalPhaseRecoveryOwner.RunAsync(Bm25ColdInvalidationTrial.RunAsync);
}
