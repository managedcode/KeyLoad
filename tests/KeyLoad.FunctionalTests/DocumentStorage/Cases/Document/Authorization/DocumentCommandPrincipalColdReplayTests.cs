namespace KeyLoad.FunctionalTests.DocumentStorage;

internal sealed class DocumentCommandPrincipalColdReplayTests
{
    [Test]
    public Task TwoPersistedPrincipalsKeepIndependentReceiptsThroughConflictsAndTwoColdReopens()
        => DocumentPatchAtomicityOwner.RunAsync(DocumentCommandPrincipalColdReplayTrial.RunAsync);
}
