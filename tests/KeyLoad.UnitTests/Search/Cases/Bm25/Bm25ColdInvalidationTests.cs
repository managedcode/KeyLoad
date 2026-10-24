namespace KeyLoad.UnitTests.Search;

internal sealed class Bm25ColdInvalidationTests
{
    [Test]
    public Task OriginalPersistedHeadFallsBackAfterDocumentAndPolicyChangesAcrossSameOwnerColdReopens()
        => Bm25OriginalPhaseRecoveryOwner.RunAsync(Bm25ColdInvalidationTrial.RunAsync);
}
